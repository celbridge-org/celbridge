using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using Celbridge.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Celbridge.Server.Services;

/// <summary>
/// Metadata extracted from tool method reflection: alias and return type.
/// </summary>
public record ToolMetadata(string Alias, string ReturnType);

/// <summary>
/// Bridges JSON-RPC tool calls from TCP clients (e.g. Python REPL) to the MCP
/// HTTP server. Registered as an RPC target on the TCP transport, it forwards
/// tools/list and tools/call requests to the MCP endpoint rather than
/// reimplementing tool discovery and invocation.
/// </summary>
public class McpToolBridge
{
    private const string McpSessionIdHeader = "Mcp-Session-Id";

    private readonly IServerService _serverService;
    private readonly ILogger<McpToolBridge> _logger;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, ToolMetadata> _toolMetadata;

    private int _nextRequestId;
    private string? _sessionId;

    public McpToolBridge(
        IServerService serverService,
        ILogger<McpToolBridge> logger)
    {
        _serverService = serverService;
        _logger = logger;
        _httpClient = new HttpClient();
        _toolMetadata = BuildToolMetadata();
    }

    [JsonRpcMethod("tools/list")]
    public async Task<object> ToolsList()
    {
        var response = await SendMcpRequestAsync("tools/list", null);
        if (response is null)
        {
            return Array.Empty<object>();
        }

        var toolsNode = response["tools"];
        if (toolsNode is not JsonArray toolsArray)
        {
            return Array.Empty<object>();
        }

        var toolInfoList = new List<object>(toolsArray.Count);
        foreach (var toolNode in toolsArray)
        {
            if (toolNode is not JsonObject tool)
            {
                continue;
            }

            var name = tool["name"]?.GetValue<string>() ?? string.Empty;
            var description = tool["description"]?.GetValue<string>() ?? string.Empty;

            var parameters = new List<object>();
            var inputSchema = tool["inputSchema"]?["properties"]?.AsObject();
            var requiredArray = tool["inputSchema"]?["required"]?.AsArray();
            var requiredNames = new HashSet<string>();
            if (requiredArray is not null)
            {
                foreach (var requiredName in requiredArray)
                {
                    var requiredString = requiredName?.GetValue<string>();
                    if (requiredString is not null)
                    {
                        requiredNames.Add(requiredString);
                    }
                }
            }

            if (inputSchema is not null)
            {
                foreach (var property in inputSchema)
                {
                    var parameterName = property.Key;
                    var parameterSchema = property.Value?.AsObject();
                    var parameterType = parameterSchema?["type"]?.GetValue<string>() ?? "string";
                    var parameterDescription = parameterSchema?["description"]?.GetValue<string>() ?? string.Empty;
                    var hasDefaultValue = !requiredNames.Contains(parameterName);

                    object? defaultValue = null;
                    if (hasDefaultValue && parameterSchema?["default"] is JsonNode defaultNode)
                    {
                        defaultValue = ConvertJsonNodeToObject(defaultNode);
                    }

                    var parameterInfo = new Dictionary<string, object?>
                    {
                        ["name"] = parameterName,
                        ["type"] = parameterType,
                        ["description"] = parameterDescription,
                        ["hasDefaultValue"] = hasDefaultValue,
                        ["defaultValue"] = defaultValue
                    };

                    // For array parameters, include the item type so clients
                    // know the element type (e.g. "array of string").
                    if (parameterType == "array")
                    {
                        var itemType = parameterSchema?["items"]?["type"]?.GetValue<string>();
                        if (itemType is not null)
                        {
                            parameterInfo["itemType"] = itemType;
                        }
                    }

                    parameters.Add(parameterInfo);
                }
            }

            _toolMetadata.TryGetValue(name, out var metadata);
            var alias = metadata?.Alias ?? string.Empty;
            var returnType = metadata?.ReturnType ?? string.Empty;

            var toolInfo = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["alias"] = alias,
                ["description"] = description,
                ["returnType"] = returnType,
                ["parameters"] = parameters
            };
            toolInfoList.Add(toolInfo);
        }

        return toolInfoList;
    }

    [JsonRpcMethod("tools/call")]
    public async Task<object> ToolsCall(string name, JObject? arguments)
    {
        var argumentsNode = ConvertJObjectToJsonObject(arguments);

        var callParams = new JsonObject
        {
            ["name"] = name
        };
        if (argumentsNode is not null)
        {
            callParams["arguments"] = argumentsNode;
        }

        try
        {
            var response = await SendMcpRequestAsync("tools/call", callParams);
            if (response is null)
            {
                return new Dictionary<string, object?>
                {
                    ["isSuccess"] = false,
                    ["errorMessage"] = "No response from MCP server",
                    ["value"] = null
                };
            }

            var isError = response["isError"]?.GetValue<bool>() ?? false;
            var contentArray = response["content"]?.AsArray();

            var contentBlocks = new List<Dictionary<string, object?>>();
            string? firstTextValue = null;

            if (contentArray is not null)
            {
                foreach (var contentItem in contentArray)
                {
                    if (contentItem is not JsonObject contentObject)
                    {
                        continue;
                    }

                    var contentType = contentObject["type"]?.GetValue<string>() ?? "";
                    var block = new Dictionary<string, object?>
                    {
                        ["type"] = contentType
                    };

                    if (contentType == "text")
                    {
                        var text = contentObject["text"]?.GetValue<string>();
                        block["text"] = text;
                        firstTextValue ??= text;
                    }
                    else if (contentType == "image")
                    {
                        block["data"] = contentObject["data"]?.GetValue<string>();
                        block["mimeType"] = contentObject["mimeType"]?.GetValue<string>();
                    }
                    else if (contentType == "resource")
                    {
                        block["resource"] = contentObject["resource"]?.ToJsonString();
                    }

                    contentBlocks.Add(block);
                }
            }

            return new Dictionary<string, object?>
            {
                ["isSuccess"] = !isError,
                ["errorMessage"] = isError ? (firstTextValue ?? "Tool call failed") : string.Empty,
                ["value"] = isError ? null : firstTextValue,
                ["content"] = contentBlocks
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Tool '{ToolName}' failed", name);
            return new Dictionary<string, object?>
            {
                ["isSuccess"] = false,
                ["errorMessage"] = exception.Message,
                ["value"] = null
            };
        }
    }

    /// <summary>
    /// Ensures an MCP session is established by sending an initialize request
    /// if we don't already have a session ID.
    /// </summary>
    private async Task EnsureSessionAsync(string url)
    {
        if (_sessionId is not null)
        {
            return;
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);

        var initializeRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "initialize",
            ["id"] = requestId,
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2025-03-26",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "CelbridgeMcpToolBridge",
                    ["version"] = "1.0.0"
                }
            }
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(initializeRequest);
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");

        var httpResponse = await _httpClient.SendAsync(httpRequest);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync();
            _logger.LogWarning("MCP initialize returned {StatusCode}: {Body}",
                (int)httpResponse.StatusCode, errorBody);
            httpResponse.EnsureSuccessStatusCode();
        }

        if (httpResponse.Headers.TryGetValues(McpSessionIdHeader, out var sessionValues))
        {
            _sessionId = sessionValues.FirstOrDefault();
        }

        // Send the initialized notification to complete the handshake
        var notificationId = Interlocked.Increment(ref _nextRequestId);
        var initializedNotification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };

        var notifyRequest = new HttpRequestMessage(HttpMethod.Post, url);
        notifyRequest.Content = JsonContent.Create(initializedNotification);
        notifyRequest.Headers.Accept.Clear();
        notifyRequest.Headers.Accept.ParseAdd("application/json");
        notifyRequest.Headers.Accept.ParseAdd("text/event-stream");
        if (_sessionId is not null)
        {
            notifyRequest.Headers.Add(McpSessionIdHeader, _sessionId);
        }

        await _httpClient.SendAsync(notifyRequest);
    }

    private async Task<JsonObject?> SendMcpRequestAsync(string method, JsonObject? methodParams)
    {
        var port = _serverService.Port;
        if (port == 0)
        {
            _logger.LogWarning("MCP HTTP server not started, cannot forward {Method}", method);
            return null;
        }

        var url = $"http://127.0.0.1:{port}/mcp";

        await EnsureSessionAsync(url);

        var requestId = Interlocked.Increment(ref _nextRequestId);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = requestId
        };
        if (methodParams is not null)
        {
            request["params"] = methodParams;
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(request);
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");
        if (_sessionId is not null)
        {
            httpRequest.Headers.Add(McpSessionIdHeader, _sessionId);
        }

        var httpResponse = await _httpClient.SendAsync(httpRequest);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "MCP {Method} returned {StatusCode}: {Body}",
                method, (int)httpResponse.StatusCode, errorBody);
            httpResponse.EnsureSuccessStatusCode();
        }

        var responseBody = await httpResponse.Content.ReadAsStringAsync();

        // The MCP Streamable HTTP transport may respond with SSE (text/event-stream)
        // or plain JSON (application/json). When SSE, extract the JSON from the
        // "data:" line of the last event.
        var contentType = httpResponse.Content.Headers.ContentType?.MediaType ?? "";
        string responseJson;
        if (contentType.Contains("text/event-stream"))
        {
            responseJson = ExtractLastSseData(responseBody);
        }
        else
        {
            responseJson = responseBody;
        }

        var responseNode = JsonNode.Parse(responseJson);

        return responseNode?["result"]?.AsObject();
    }

    /// <summary>
    /// Extracts the JSON payload from the last "data:" line in an SSE response body.
    /// SSE events have the format: "event: message\ndata: {json}\n\n"
    /// </summary>
    private static string ExtractLastSseData(string sseBody)
    {
        string lastData = "";
        foreach (var line in sseBody.Split('\n'))
        {
            if (line.StartsWith("data: "))
            {
                lastData = line.Substring(6);
            }
        }

        return lastData;
    }

    /// <summary>
    /// Converts a JsonNode to a plain CLR object (string, bool, number, or null).
    /// Prevents JsonElement serialization artifacts like {"ValueKind": 5}.
    /// </summary>
    private static object? ConvertJsonNodeToObject(JsonNode node)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }
            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }
            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }
            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }
        }

        return null;
    }

    private static JsonObject? ConvertJObjectToJsonObject(JObject? jObject)
    {
        if (jObject is null)
        {
            return null;
        }

        var jsonString = jObject.ToString();
        return JsonNode.Parse(jsonString)?.AsObject();
    }

    /// <summary>
    /// Maps a C# return type to a JSON Schema type name.
    /// Client-side code is responsible for mapping these to language-specific names.
    /// </summary>
    private static string MapReturnTypeToJsonSchema(Type returnType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task))
        {
            return "";
        }

        var actualType = returnType;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            actualType = returnType.GetGenericArguments()[0];
        }

        if (actualType == typeof(string))
        {
            return "string";
        }
        if (actualType == typeof(bool))
        {
            return "boolean";
        }
        if (actualType == typeof(int) || actualType == typeof(long))
        {
            return "integer";
        }
        if (actualType == typeof(float) || actualType == typeof(double))
        {
            return "number";
        }

        return "string";
    }

    /// <summary>
    /// Scans all McpServerTool methods in the Tools assembly for ToolAlias
    /// attributes and builds a mapping of MCP tool name to metadata.
    /// </summary>
    private static Dictionary<string, ToolMetadata> BuildToolMetadata()
    {
        var mapping = new Dictionary<string, ToolMetadata>();
        var assembly = typeof(AppTools).Assembly;

        var toolTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        foreach (var toolType in toolTypes)
        {
            var toolMethods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

            foreach (var method in toolMethods)
            {
                var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
                var aliasAttribute = method.GetCustomAttribute<ToolAliasAttribute>();

                var toolName = toolAttribute.Name ?? method.Name;
                var alias = aliasAttribute?.Alias ?? string.Empty;
                var returnType = MapReturnTypeToJsonSchema(method.ReturnType);

                if (!string.IsNullOrEmpty(alias))
                {
                    mapping[toolName] = new ToolMetadata(alias, returnType);
                }
            }
        }

        return mapping;
    }
}
