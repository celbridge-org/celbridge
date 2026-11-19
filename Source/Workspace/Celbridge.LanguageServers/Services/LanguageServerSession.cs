using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Celbridge.LanguageServers.Helpers;
using Celbridge.Logging;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// One running language server and the pages connected to it. The session is the server's one Language Server
/// Protocol client, and every page reaches the server through the session.
/// </summary>
internal sealed class LanguageServerSession
{
    private enum SessionState
    {
        Starting,
        Ready,
        Stopping,
        Stopped
    }

    private enum PendingKind
    {
        Initialize,
        PageRequest,
        Diagnostics,
        Shutdown
    }

    private sealed record PendingRequest(PendingKind Kind, PageConnection? Page, JsonNode? PageRequestId);

    private const string ClientName = "Celbridge";
    private const string TextDocumentPrefix = "textDocument/";
    private const string ResolveSuffix = "/resolve";

    // The property of a page's request that names the document. The session passes a page's parameters on unread,
    // apart from this one property.
    private const string TextDocumentProperty = "textDocument";

    // The kind of a diagnostics answer that carries the document's diagnostics.
    private const string FullReportKind = "full";

    // JSON-RPC and Language Server Protocol error codes.
    private const int MethodNotFoundCode = -32601;
    private const int RequestFailedCode = -32803;
    private const int ServerCancelledCode = -32802;

    // The type of a server's log message that reports an error.
    private const int ErrorMessageType = 1;

    // The session alone sends the document notifications and the diagnostics request. A page's request with one of
    // these methods is refused.
    private static readonly HashSet<string> SessionOwnedMethods = new()
    {
        LspMethods.DidOpen,
        LspMethods.DidChange,
        LspMethods.DidClose,
        LspMethods.DidSave,
        LspMethods.WillSave,
        LspMethods.WillSaveWaitUntil,
        LspMethods.Diagnostic,
    };

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);

    private readonly ILanguageServerDefinition _definition;
    private readonly ILanguageServerProcess _process;
    private readonly string _projectFolderPath;
    private readonly string _clientVersion;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Action<LanguageServerSession, bool> _onEnded;

    private readonly LspMessageStream _messageStream;
    private readonly Channel<string> _outgoingMessages;
    private readonly Lock _lock = new();
    private readonly List<PageConnection> _pages = new();
    private readonly Dictionary<long, PendingRequest> _pendingRequests = new();

    private SessionState _state = SessionState.Starting;
    private long _nextRequestId = 1;
    private JsonObject _serverCapabilities = new();
    private bool _pullsDiagnostics;
    private string? _diagnosticsIdentifier;
    private int _roundOutstandingCount;
    private bool _roundRequested;
    private Task _runTask = Task.CompletedTask;

    /// <summary>
    /// Creates a session for a server that has just started. The callback runs once, when the session ends, with
    /// true if the server ended unexpectedly.
    /// </summary>
    public LanguageServerSession(
        ILanguageServerDefinition definition,
        ILanguageServerProcess process,
        string projectFolderPath,
        string clientVersion,
        ILogger logger,
        TimeProvider timeProvider,
        Action<LanguageServerSession, bool> onEnded)
    {
        _definition = definition;
        _process = process;
        _projectFolderPath = projectFolderPath;
        _clientVersion = clientVersion;
        _logger = logger;
        _timeProvider = timeProvider;
        _onEnded = onEnded;

        _messageStream = new LspMessageStream(process.Input, process.Output);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _outgoingMessages = Channel.CreateUnbounded<string>(channelOptions);
    }

    public string ServerId => _definition.Id;

    /// <summary>
    /// Sends the initialize request, and starts reading the server's messages.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            var workspaceFolders = new List<WorkspaceFolder>
            {
                CreateWorkspaceFolder(),
            };

            var initializeParams = new InitializeParams(
                Environment.ProcessId,
                new ClientInfo(ClientName, _clientVersion),
                LspUri.FromPath(_projectFolderPath),
                _projectFolderPath,
                workspaceFolders,
                ClientCapabilities.Create(),
                _definition.CreateInitializationOptions());

            var pendingRequest = new PendingRequest(PendingKind.Initialize, null, null);
            SendRequest(LspMethods.Initialize, initializeParams, pendingRequest);
        }

        _runTask = Task.Run(RunAsync);
    }

    /// <summary>
    /// Connects a page's document to the server. The page gets the server's capabilities once the server is ready.
    /// Returns null when the session has ended.
    /// </summary>
    public ILanguagePageConnection? AddPage(string documentUri, string languageId)
    {
        lock (_lock)
        {
            if (_state == SessionState.Stopping
                || _state == SessionState.Stopped)
            {
                return null;
            }

            var page = new PageConnection(this, documentUri, languageId);
            _pages.Add(page);

            if (_state == SessionState.Ready)
            {
                SendServerReady(page);
            }

            return page;
        }
    }

    /// <summary>
    /// Handles one message from a page: the document's text, a change, a request or a cancellation.
    /// </summary>
    public void HandlePageMessage(PageConnection page, string messageText)
    {
        LspIncomingMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<LspIncomingMessage>(messageText, LspJson.Options);
        }
        catch (JsonException exception)
        {
            _logger.LogDebug(exception, "A page sent a message that is not a JSON-RPC message");
            return;
        }

        // Every message from a page has a method, since a page answers no requests.
        if (message is null
            || string.IsNullOrEmpty(message.Method))
        {
            return;
        }

        lock (_lock)
        {
            if (_state != SessionState.Ready
                || !_pages.Contains(page))
            {
                if (message.Id is not null)
                {
                    SendErrorToPage(page, message.Id, RequestFailedCode, "The language server is not running");
                }
                return;
            }

            if (message.Id is null)
            {
                HandlePageNotification(page, message.Method, message.Params);
            }
            else
            {
                HandlePageRequest(page, message.Method, message.Id, message.Params);
            }
        }
    }

    /// <summary>
    /// Disconnects a page. The server closes the page's document, and cancels the page's unanswered requests.
    /// </summary>
    public void RemovePage(PageConnection page)
    {
        lock (_lock)
        {
            if (!_pages.Remove(page))
            {
                return;
            }

            if (_state != SessionState.Ready)
            {
                return;
            }

            foreach (var serverRequestId in page.PendingRequests.Values)
            {
                SendCancellation(serverRequestId);
            }
            page.PendingRequests.Clear();

            if (page.IsOpen)
            {
                page.IsOpen = false;
                SendDidClose(page);

                // The server reads the file from disk again, so the other documents may show other errors.
                RequestDiagnosticsRound();
            }
        }
    }

    /// <summary>
    /// Stops the server. Asks the server to shut down and exit, and ends the process if the server is still running
    /// when the stop timeout ends.
    /// </summary>
    public async Task StopAsync()
    {
        var killNow = false;
        List<PageConnection> pages;

        lock (_lock)
        {
            if (_state == SessionState.Stopping
                || _state == SessionState.Stopped)
            {
                return;
            }

            // A server accepts a shutdown request only after the server has answered initialize.
            killNow = _state == SessionState.Starting;
            _state = SessionState.Stopping;

            pages = new List<PageConnection>(_pages);
            _pages.Clear();

            if (!killNow)
            {
                SendRequest(LspMethods.Shutdown, null, new PendingRequest(PendingKind.Shutdown, null, null));
            }
        }

        foreach (var page in pages)
        {
            page.Outbox.Complete();
        }

        if (killNow)
        {
            _process.Kill();
        }
        else
        {
            var timeoutTask = Task.Delay(StopTimeout, _timeProvider);
            var finishedTask = await Task.WhenAny(_process.Exited, timeoutTask);
            if (finishedTask != _process.Exited)
            {
                _logger.LogDebug("The language server '{ServerId}' did not exit in time", ServerId);
                _process.Kill();
            }
        }

        await _runTask;
    }

    // Reads the server's messages until the server closes its output, and then ends the session.
    private async Task RunAsync()
    {
        var writeTask = Task.Run(WriteLoopAsync);

        try
        {
            while (true)
            {
                var message = await _messageStream.ReadAsync(CancellationToken.None);
                if (message is null)
                {
                    break;
                }

                HandleServerMessage(message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read from the language server '{ServerId}'", ServerId);
        }

        bool crashed;
        List<PageConnection> pages;

        lock (_lock)
        {
            crashed = _state != SessionState.Stopping;
            _state = SessionState.Stopped;

            pages = new List<PageConnection>(_pages);
            _pages.Clear();
            _pendingRequests.Clear();
        }

        _outgoingMessages.Writer.TryComplete();

        if (crashed)
        {
            _logger.LogWarning("The language server '{ServerId}' ended unexpectedly", ServerId);
            _process.Kill();
        }

        await writeTask;

        var timeoutTask = Task.Delay(ExitTimeout, _timeProvider);
        await Task.WhenAny(_process.Exited, timeoutTask);
        _process.Dispose();

        // The owner learns of the end before the pages do. A page connects again as soon as its connection closes.
        _onEnded(this, crashed);

        foreach (var page in pages)
        {
            page.Outbox.Complete();
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var message in _outgoingMessages.Reader.ReadAllAsync())
            {
                await _messageStream.WriteAsync(message, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            // The server has closed its input. The read loop sees the server end, and ends the session.
            _logger.LogDebug(exception, "Failed to write to the language server '{ServerId}'", ServerId);
        }
    }

    private void HandlePageNotification(PageConnection page, string method, JsonNode? parameters)
    {
        if (method == LspMethods.DidOpen)
        {
            if (!LspJson.TryRead<PageDidOpenParams>(parameters, out var openParams))
            {
                return;
            }

            if (page.IsOpen)
            {
                SendDidClose(page);
            }

            var textDocument = new TextDocumentItem(
                page.DocumentUri,
                page.LanguageId,
                openParams.Version,
                openParams.Text ?? string.Empty);
            SendNotification(LspMethods.DidOpen, new DidOpenParams(textDocument));

            page.IsOpen = true;
            page.DiagnosticsResultId = string.Empty;
            RequestDiagnosticsRound();
            return;
        }

        if (method == LspMethods.DidChange)
        {
            if (!page.IsOpen
                || !LspJson.TryRead<PageDidChangeParams>(parameters, out var changeParams)
                || changeParams.ContentChanges is not JsonArray contentChanges)
            {
                return;
            }

            var textDocument = new VersionedTextDocumentIdentifier(page.DocumentUri, changeParams.Version);
            SendNotification(LspMethods.DidChange, new DidChangeParams(textDocument, contentChanges));

            RequestDiagnosticsRound();
            return;
        }

        if (method == LspMethods.CancelRequest)
        {
            if (LspJson.TryRead<PageCancelParams>(parameters, out var cancelParams)
                && cancelParams.Id is not null
                && page.PendingRequests.TryGetValue(cancelParams.Id.ToJsonString(), out var serverRequestId))
            {
                SendCancellation(serverRequestId);
            }
        }
    }

    private void HandlePageRequest(PageConnection page, string method, JsonNode requestId, JsonNode? parameters)
    {
        if (!IsPageRequestAllowed(method))
        {
            SendErrorToPage(page, requestId, MethodNotFoundCode, $"A page may not send '{method}'");
            return;
        }

        if (!page.IsOpen)
        {
            SendErrorToPage(page, requestId, RequestFailedCode, "The document is not open on the language server");
            return;
        }

        // A request about a document names the page's own document, whatever the page sent.
        if (method.StartsWith(TextDocumentPrefix, StringComparison.Ordinal))
        {
            var parameterObject = parameters as JsonObject ?? new JsonObject();
            var textDocument = new TextDocumentIdentifier(page.DocumentUri);
            parameterObject[TextDocumentProperty] = JsonSerializer.SerializeToNode(textDocument, LspJson.Options);
            parameters = parameterObject;
        }

        var pendingRequest = new PendingRequest(PendingKind.PageRequest, page, requestId);
        var serverRequestId = SendRequest(method, parameters, pendingRequest);
        page.PendingRequests[requestId.ToJsonString()] = serverRequestId;
    }

    private static bool IsPageRequestAllowed(string method)
    {
        if (SessionOwnedMethods.Contains(method))
        {
            return false;
        }

        return method.StartsWith(TextDocumentPrefix, StringComparison.Ordinal)
            || method.EndsWith(ResolveSuffix, StringComparison.Ordinal);
    }

    private void HandleServerMessage(JsonObject messageObject)
    {
        if (!LspJson.TryRead<LspIncomingMessage>(messageObject, out var message))
        {
            _logger.LogDebug("The language server '{ServerId}' sent a message of an unknown shape", ServerId);
            return;
        }

        lock (_lock)
        {
            if (string.IsNullOrEmpty(message.Method))
            {
                HandleServerResponse(message);
            }
            else if (message.Id is null)
            {
                HandleServerNotification(message.Method, message.Params);
            }
            else
            {
                HandleServerRequest(message.Method, message.Id, message.Params);
            }
        }
    }

    private void HandleServerResponse(LspIncomingMessage message)
    {
        if (message.Id is null
            || message.Id.GetValueKind() != JsonValueKind.Number
            || !_pendingRequests.Remove(message.Id.GetValue<long>(), out var pendingRequest))
        {
            return;
        }

        switch (pendingRequest.Kind)
        {
            case PendingKind.Initialize:
                CompleteInitialize(message.Result, message.Error);
                break;

            case PendingKind.PageRequest:
                CompletePageRequest(pendingRequest, message.Result, message.Error);
                break;

            case PendingKind.Diagnostics:
                CompleteDiagnosticsRequest(pendingRequest.Page!, message.Result, message.Error);
                break;

            case PendingKind.Shutdown:
                SendNotification(LspMethods.Exit, null);
                break;
        }
    }

    private void CompleteInitialize(JsonNode? result, LspError? error)
    {
        if (_state != SessionState.Starting)
        {
            return;
        }

        if (!LspJson.TryRead<InitializeResult>(result, out var initializeResult))
        {
            var errorText = "no result";
            if (error is not null)
            {
                errorText = LspJson.Serialize(error);
            }

            _logger.LogWarning("The language server '{ServerId}' refused initialize: {Error}", ServerId, errorText);
            _process.Kill();
            return;
        }

        _serverCapabilities = initializeResult.Capabilities ?? new JsonObject();

        // A server offers pulled diagnostics with true or with an options object.
        if (LspJson.TryRead<ServerCapabilities>(_serverCapabilities, out var capabilities)
            && capabilities.DiagnosticProvider is JsonNode diagnosticProvider
            && diagnosticProvider.GetValueKind() != JsonValueKind.False)
        {
            _pullsDiagnostics = true;

            if (LspJson.TryRead<DiagnosticProviderOptions>(diagnosticProvider, out var diagnosticOptions))
            {
                _diagnosticsIdentifier = diagnosticOptions.Identifier;
            }
        }

        SendNotification(LspMethods.Initialized, new JsonObject());
        _state = SessionState.Ready;

        _logger.LogInformation("The language server '{ServerId}' is ready", ServerId);

        foreach (var page in _pages)
        {
            SendServerReady(page);
        }
    }

    private void CompletePageRequest(PendingRequest pendingRequest, JsonNode? result, LspError? error)
    {
        var page = pendingRequest.Page!;
        var pageRequestId = pendingRequest.PageRequestId!;

        page.PendingRequests.Remove(pageRequestId.ToJsonString());

        if (!_pages.Contains(page))
        {
            return;
        }

        if (error is not null)
        {
            SendToPage(page, new LspErrorResponse(pageRequestId, error));
            return;
        }

        SendToPage(page, new LspResponse(pageRequestId, result));
    }

    private void CompleteDiagnosticsRequest(PageConnection page, JsonNode? result, LspError? error)
    {
        if (_pages.Contains(page)
            && LspJson.TryRead<DiagnosticReport>(result, out var report)
            && report.Kind == FullReportKind)
        {
            page.DiagnosticsResultId = report.ResultId ?? string.Empty;
            SendDiagnosticsToPage(page, report.Items ?? new JsonArray());
        }

        if (error is not null
            && error.Code == ServerCancelledCode
            && AllowsRetry(error))
        {
            _roundRequested = true;
        }

        _roundOutstandingCount--;
        if (_roundOutstandingCount == 0
            && _roundRequested)
        {
            StartDiagnosticsRound();
        }
    }

    private void HandleServerRequest(string method, JsonNode requestId, JsonNode? parameters)
    {
        switch (method)
        {
            case LspMethods.Configuration:
                var settings = new List<JsonNode?>();
                if (LspJson.TryRead<ConfigurationParams>(parameters, out var configurationParams)
                    && configurationParams.Items is not null)
                {
                    foreach (var item in configurationParams.Items)
                    {
                        var section = item?.Section ?? string.Empty;
                        settings.Add(_definition.CreateConfiguration(section));
                    }
                }
                SendResponse(requestId, settings);
                break;

            case LspMethods.WorkspaceFolders:
                var workspaceFolders = new List<WorkspaceFolder>
                {
                    CreateWorkspaceFolder(),
                };
                SendResponse(requestId, workspaceFolders);
                break;

            case LspMethods.WorkDoneProgressCreate:
            case LspMethods.RegisterCapability:
            case LspMethods.UnregisterCapability:
                SendResponse(requestId, null);
                break;

            case LspMethods.DiagnosticRefresh:
                SendResponse(requestId, null);
                RequestDiagnosticsRound();
                break;

            default:
                SendErrorResponse(requestId, MethodNotFoundCode, $"Celbridge does not handle '{method}'");
                break;
        }
    }

    private void HandleServerNotification(string method, JsonNode? parameters)
    {
        if (method == LspMethods.LogMessage
            || method == LspMethods.ShowMessage)
        {
            if (!LspJson.TryRead<LogMessageParams>(parameters, out var logParams))
            {
                return;
            }

            if (logParams.Type == ErrorMessageType)
            {
                _logger.LogWarning("Language server '{ServerId}': {Message}", ServerId, logParams.Message);
            }
            else
            {
                _logger.LogDebug("Language server '{ServerId}': {Message}", ServerId, logParams.Message);
            }
            return;
        }

        // A server without pulled diagnostics sends each document's diagnostics by itself.
        if (method == LspMethods.PublishDiagnostics)
        {
            if (!LspJson.TryRead<PublishDiagnosticsParams>(parameters, out var publishParams))
            {
                return;
            }

            var documentUri = publishParams.Uri ?? string.Empty;
            var page = _pages.FirstOrDefault(candidate => LspUri.AreSameFile(candidate.DocumentUri, documentUri));
            if (page is null)
            {
                return;
            }

            SendDiagnosticsToPage(page, publishParams.Diagnostics ?? new JsonArray());
        }
    }

    // A server that cancels a diagnostics request expects the request again. The error's data can say that the
    // client must send no retry.
    private static bool AllowsRetry(LspError error)
    {
        if (LspJson.TryRead<DiagnosticCancellationData>(error.Data, out var cancellationData)
            && cancellationData.RetriggerRequest == false)
        {
            return false;
        }

        return true;
    }

    // Starts a round of diagnostics requests, or marks one more round to run when the running round ends.
    private void RequestDiagnosticsRound()
    {
        if (!_pullsDiagnostics)
        {
            return;
        }

        if (_roundOutstandingCount > 0)
        {
            _roundRequested = true;
            return;
        }

        StartDiagnosticsRound();
    }

    // Asks the server for the diagnostics of every open document.
    private void StartDiagnosticsRound()
    {
        _roundRequested = false;

        foreach (var page in _pages)
        {
            if (!page.IsOpen)
            {
                continue;
            }

            string? previousResultId = null;
            if (!string.IsNullOrEmpty(page.DiagnosticsResultId))
            {
                previousResultId = page.DiagnosticsResultId;
            }

            var parameters = new DiagnosticParams(
                new TextDocumentIdentifier(page.DocumentUri),
                _diagnosticsIdentifier,
                previousResultId);

            SendRequest(LspMethods.Diagnostic, parameters, new PendingRequest(PendingKind.Diagnostics, page, null));
            _roundOutstandingCount++;
        }
    }

    private static void SendDiagnosticsToPage(PageConnection page, JsonNode diagnostics)
    {
        var parameters = new PageDiagnosticsParams(diagnostics);
        SendToPage(page, new LspNotification(LspMethods.PublishDiagnostics, parameters));
    }

    private void SendServerReady(PageConnection page)
    {
        var parameters = new ServerReadyParams(ServerId, _serverCapabilities);
        SendToPage(page, new LspNotification(PageMessages.ServerReadyMethod, parameters));
    }

    private static void SendErrorToPage(PageConnection page, JsonNode requestId, int code, string errorMessage)
    {
        var error = new LspError(code, errorMessage);
        SendToPage(page, new LspErrorResponse(requestId, error));
    }

    private static void SendToPage(PageConnection page, object message)
    {
        page.Outbox.Enqueue(LspJson.Serialize(message));
    }

    private long SendRequest(string method, object? parameters, PendingRequest pendingRequest)
    {
        var requestId = _nextRequestId;
        _nextRequestId++;

        _pendingRequests[requestId] = pendingRequest;
        SendToServer(new LspRequest(requestId, method, parameters));

        return requestId;
    }

    private void SendNotification(string method, object? parameters)
    {
        SendToServer(new LspNotification(method, parameters));
    }

    private void SendResponse(JsonNode requestId, object? result)
    {
        SendToServer(new LspResponse(requestId, result));
    }

    private void SendErrorResponse(JsonNode requestId, int code, string errorMessage)
    {
        var error = new LspError(code, errorMessage);
        SendToServer(new LspErrorResponse(requestId, error));
    }

    private void SendCancellation(long serverRequestId)
    {
        SendNotification(LspMethods.CancelRequest, new CancelParams(serverRequestId));
    }

    private void SendDidClose(PageConnection page)
    {
        var textDocument = new TextDocumentIdentifier(page.DocumentUri);
        SendNotification(LspMethods.DidClose, new DidCloseParams(textDocument));
    }

    private void SendToServer(object message)
    {
        _outgoingMessages.Writer.TryWrite(LspJson.Serialize(message));
    }

    private WorkspaceFolder CreateWorkspaceFolder()
    {
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_projectFolderPath));

        return new WorkspaceFolder(LspUri.FromPath(_projectFolderPath), folderName);
    }
}
