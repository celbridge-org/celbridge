using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// JSON-RPC 2.0 bridge for WebView2 communication.
/// Provides typed handler registration and automatic request/response correlation.
/// </summary>
public class WebViewBridge : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IWebViewMessageChannel _channel;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();
    private readonly ConcurrentDictionary<string, Action<JsonElement?>> _notificationHandlers = new();
    private bool _disposed;

    /// <summary>
    /// Gets or sets whether detailed logging is enabled.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>
    /// Document-related operations.
    /// </summary>
    public DocumentHandlers Document { get; }

    /// <summary>
    /// Dialog-related operations.
    /// </summary>
    public DialogHandlers Dialog { get; }

    /// <summary>
    /// Creates a new WebViewBridge with the specified message channel.
    /// </summary>
    public WebViewBridge(IWebViewMessageChannel channel, ILogger? logger = null)
    {
        _channel = channel;
        _logger = logger;

        Document = new DocumentHandlers(this);
        Dialog = new DialogHandlers(this);

        _channel.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Registers a handler for the bridge/initialize request.
    /// </summary>
    public void OnInitialize(Func<InitializeParams, Task<InitializeResult>> handler)
    {
        RegisterHandler("bridge/initialize", handler);
    }

    /// <summary>
    /// Registers a typed handler for a specific method.
    /// </summary>
    public void RegisterHandler<TParams, TResult>(string method, Func<TParams, Task<TResult>> handler)
    {
        _handlers[method] = async (paramsElement) =>
        {
            var parameters = paramsElement.HasValue
                ? JsonSerializer.Deserialize<TParams>(paramsElement.Value.GetRawText(), _jsonOptions)
                : default;
            var result = await handler(parameters!);
            return result;
        };

        LogDebug($"Registered handler for method: {method}");
    }

    /// <summary>
    /// Registers a notification handler (no response expected).
    /// </summary>
    public void RegisterNotificationHandler<TParams>(string method, Action<TParams> handler)
    {
        _notificationHandlers[method] = (paramsElement) =>
        {
            var parameters = paramsElement.HasValue
                ? JsonSerializer.Deserialize<TParams>(paramsElement.Value.GetRawText(), _jsonOptions)
                : default;
            handler(parameters!);
        };

        LogDebug($"Registered notification handler for method: {method}");
    }

    /// <summary>
    /// Sends a notification to the WebView (no response expected).
    /// </summary>
    public void SendNotification<T>(string method, T parameters)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = JsonSerializer.SerializeToNode(parameters, _jsonOptions)
        };

        var json = message.ToJsonString(_jsonOptions);
        LogDebug($"→ notification: {method}");
        _channel.PostMessage(json);
    }

    /// <summary>
    /// Sends a notification to the WebView with no parameters.
    /// </summary>
    public void SendNotification(string method)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        var json = message.ToJsonString(_jsonOptions);
        LogDebug($"→ notification: {method}");
        _channel.PostMessage(json);
    }

    private async void OnMessageReceived(object? sender, string json)
    {
        try
        {
            await HandleMessageAsync(json);
        }
        catch (Exception ex)
        {
            LogError($"Error handling message: {ex.Message}");
        }
    }

    private async Task HandleMessageAsync(string json)
    {
        JsonDocument? doc = null;
        JsonElement? id = null;

        try
        {
            doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract common fields
            var hasId = root.TryGetProperty("id", out var idElement);
            if (hasId)
            {
                id = idElement;
            }

            if (!root.TryGetProperty("method", out var methodElement))
            {
                if (hasId)
                {
                    SendErrorResponse(id, JsonRpcErrorCodes.InvalidRequest, "Missing method field");
                }
                return;
            }

            var method = methodElement.GetString();
            if (string.IsNullOrEmpty(method))
            {
                if (hasId)
                {
                    SendErrorResponse(id, JsonRpcErrorCodes.InvalidRequest, "Empty method field");
                }
                return;
            }

            // Extract params (optional)
            JsonElement? paramsElement = null;
            if (root.TryGetProperty("params", out var paramsElem))
            {
                paramsElement = paramsElem;
            }

            // Notification (no id)
            if (!hasId)
            {
                LogDebug($"← notification: {method}");
                HandleNotification(method, paramsElement);
                return;
            }

            // Request (has id)
            LogDebug($"← request #{id}: {method}");
            await HandleRequestAsync(method, paramsElement, id.Value);
        }
        catch (JsonException ex)
        {
            LogError($"JSON parse error: {ex.Message}");
            SendErrorResponse(id, JsonRpcErrorCodes.ParseError, "Invalid JSON");
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private void HandleNotification(string method, JsonElement? paramsElement)
    {
        if (_notificationHandlers.TryGetValue(method, out var handler))
        {
            try
            {
                handler(paramsElement);
            }
            catch (Exception ex)
            {
                LogError($"Notification handler error for {method}: {ex.Message}");
            }
        }
        else
        {
            LogDebug($"No handler registered for notification: {method}");
        }
    }

    private async Task HandleRequestAsync(string method, JsonElement? paramsElement, JsonElement id)
    {
        var startTime = DateTime.UtcNow;

        if (!_handlers.TryGetValue(method, out var handler))
        {
            SendErrorResponse(id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}");
            return;
        }

        try
        {
            var result = await handler(paramsElement);
            SendSuccessResponse(id, result, startTime);
        }
        catch (BridgeException ex)
        {
            SendErrorResponse(id, ex.Code, ex.Message, ex.Data);
        }
        catch (Exception ex)
        {
            // Requirement 9: wrap unhandled exceptions in JSON-RPC error
            LogError($"Handler exception for {method}: {ex}");
            SendErrorResponse(id, JsonRpcErrorCodes.InternalError, ex.Message);
        }
    }

    private void SendSuccessResponse(JsonElement id, object? result, DateTime startTime)
    {
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = JsonSerializer.SerializeToNode(result, _jsonOptions),
            ["id"] = JsonNode.Parse(id.GetRawText())
        };

        var json = message.ToJsonString(_jsonOptions);
        LogDebug($"→ response #{id}: success ({elapsed:F0}ms)");
        _channel.PostMessage(json);
    }

    private void SendErrorResponse(JsonElement? id, int code, string message, object? data = null)
    {
        var errorObj = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (data != null)
        {
            errorObj["data"] = JsonSerializer.SerializeToNode(data, _jsonOptions);
        }

        var responseObj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = errorObj
        };

        if (id.HasValue)
        {
            responseObj["id"] = JsonNode.Parse(id.Value.GetRawText());
        }
        else
        {
            responseObj["id"] = null;
        }

        var json = responseObj.ToJsonString(_jsonOptions);
        LogDebug($"→ response #{id}: error {code} - {message}");
        _channel.PostMessage(json);
    }

    private void LogDebug(string message)
    {
        if (EnableDetailedLogging)
        {
            _logger?.LogDebug("[WebViewBridge] {Message}", message);
        }
    }

    private void LogError(string message)
    {
        _logger?.LogError("[WebViewBridge] {Message}", message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _channel.MessageReceived -= OnMessageReceived;
        _disposed = true;
    }

    // =========================================================================
    // Nested Handler Classes for Fluent API
    // =========================================================================

    /// <summary>
    /// Document-related handler registration.
    /// </summary>
    public class DocumentHandlers
    {
        private readonly WebViewBridge _bridge;

        internal DocumentHandlers(WebViewBridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// Registers a handler for document/load requests.
        /// </summary>
        public void OnLoad(Func<LoadParams, Task<LoadResult>> handler)
        {
            _bridge.RegisterHandler("document/load", handler);
        }

        /// <summary>
        /// Registers a handler for document/save requests.
        /// </summary>
        public void OnSave(Func<SaveParams, Task<SaveResult>> handler)
        {
            _bridge.RegisterHandler("document/save", handler);
        }

        /// <summary>
        /// Registers a handler for document/getMetadata requests.
        /// </summary>
        public void OnGetMetadata(Func<GetMetadataParams, Task<DocumentMetadata>> handler)
        {
            _bridge.RegisterHandler("document/getMetadata", handler);
        }

        /// <summary>
        /// Registers a handler for document/changed notifications.
        /// </summary>
        public void OnChanged(Action handler)
        {
            _bridge.RegisterNotificationHandler<DocumentChangedNotification>("document/changed", _ => handler());
        }

        /// <summary>
        /// Sends a document/externalChange notification to the WebView.
        /// </summary>
        public void NotifyExternalChange()
        {
            _bridge.SendNotification("document/externalChange");
        }
    }

    /// <summary>
    /// Dialog-related handler registration.
    /// </summary>
    public class DialogHandlers
    {
        private readonly WebViewBridge _bridge;

        internal DialogHandlers(WebViewBridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// Registers a handler for dialog/pickImage requests.
        /// </summary>
        public void OnPickImage(Func<PickImageParams, Task<PickImageResult>> handler)
        {
            _bridge.RegisterHandler("dialog/pickImage", handler);
        }

        /// <summary>
        /// Registers a handler for dialog/pickFile requests.
        /// </summary>
        public void OnPickFile(Func<PickFileParams, Task<PickFileResult>> handler)
        {
            _bridge.RegisterHandler("dialog/pickFile", handler);
        }

        /// <summary>
        /// Registers a handler for dialog/alert requests.
        /// </summary>
        public void OnAlert(Func<AlertParams, Task<AlertResult>> handler)
        {
            _bridge.RegisterHandler("dialog/alert", handler);
        }
    }
}
