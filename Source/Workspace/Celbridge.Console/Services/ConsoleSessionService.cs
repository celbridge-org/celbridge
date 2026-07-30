using Celbridge.Console.Helpers;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Server;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

/// <summary>
/// Workspace-scoped owner of the running console sessions. Lifetime is driven by document open and close
/// messages rather than by the views, so a console in a background tab is running like any other.
/// </summary>
public sealed class ConsoleSessionService : IConsoleSessionService, IDisposable
{
    private const string ConsoleFileExtension = ".console";

    // A headless session has no view to measure, so it starts at a nominal size and is resized to the
    // real one the moment a view attaches. Only output produced before that first attach is affected,
    // and the terminal reflows it on the resize.
    private const int DefaultCols = 120;
    private const int DefaultRows = 30;

    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<ConsoleSessionService> _logger;

    private readonly object _sessionsLock = new();
    private readonly Dictionary<ResourceKey, ConsoleLiveSession> _sessions = new();

    // Maps a bound transport connection to the session that launched it. Only sessions whose client has
    // connected appear here.
    private readonly Dictionary<int, Guid> _connectionToSession = new();

    private readonly ConsoleProxyListener _proxyListener;

    private bool _disposed;

    public ConsoleSessionService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        ILogger<ConsoleSessionService> logger)
    {
        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _logger = logger;

        var tcpTransport = serviceProvider.GetRequiredService<ITcpTransport>();
        var listenerLogger = serviceProvider.GetRequiredService<ILogger<ConsoleProxyListener>>();
        _proxyListener = new ConsoleProxyListener(tcpTransport, this, listenerLogger);

        _messengerService.Register<DocumentOpenedMessage>(this, OnDocumentOpened);
        _messengerService.Register<DocumentClosedMessage>(this, OnDocumentClosed);
        _messengerService.Register<DocumentResourceChangedMessage>(this, OnDocumentResourceChanged);
    }

    public async Task EnsureStartedAsync(ResourceKey resource)
    {
        // Every console shares one cel-proxy listener, so the first launch starts it and the rest reuse
        // the port.
        var rpcPort = _proxyListener.EnsureStarted();

        ConsoleLiveSession session;
        lock (_sessionsLock)
        {
            if (_disposed)
            {
                return;
            }

            if (!_sessions.TryGetValue(resource, out var existingSession))
            {
                existingSession = new ConsoleLiveSession(_serviceProvider, _workspaceWrapper, resource);
                existingSession.StateChanged += OnSessionStateChanged;
                _sessions[resource] = existingSession;
            }
            session = existingSession;

            session.StartTask ??= session.StartAsync(DefaultCols, DefaultRows, rpcPort);
        }

        try
        {
            await session.StartTask!;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start the console session for '{Resource}'", resource);
        }
    }

    public async Task<ConsoleAttachSnapshot> AttachAsync(ResourceKey resource, IConsoleView attachedView, int cols, int rows)
    {
        await EnsureStartedAsync(resource);

        ConsoleLiveSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        if (session is null)
        {
            return new ConsoleAttachSnapshot(
                ConsoleSessionRunState.Failed,
                "The console session is not available.",
                false,
                string.Empty,
                null);
        }

        session.Resize(cols, rows);

        return session.Attach(attachedView);
    }

    public void Detach(ResourceKey resource, IConsoleView attachedView)
    {
        ConsoleLiveSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        session?.Detach(attachedView);
    }

    public async Task<ConsoleAttachSnapshot> ReopenAsync(ResourceKey resource, int cols, int rows)
    {
        // A reopen is a fresh launch from the file on disk: dispose the old session and start again. The
        // caller re-attaches through the returned snapshot path.
        IConsoleView? previousView = null;
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(resource, out var existingSession))
            {
                previousView = existingSession.CurrentView;
                _sessions.Remove(resource);
                ReleaseSession(existingSession);
            }
        }

        if (previousView is null)
        {
            await EnsureStartedAsync(resource);

            ConsoleLiveSession? session;
            lock (_sessionsLock)
            {
                _sessions.TryGetValue(resource, out session);
            }

            if (session is null)
            {
                return new ConsoleAttachSnapshot(
                    ConsoleSessionRunState.Failed,
                    "The console session is not available.",
                    false,
                    string.Empty,
                    null);
            }

            session.Resize(cols, rows);
            return new ConsoleAttachSnapshot(session.State, session.Error, false, string.Empty, session.LaunchedConfigToml);
        }

        return await AttachAsync(resource, previousView, cols, rows);
    }

    public void Input(ResourceKey resource, string data)
    {
        ConsoleLiveSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        session?.Input(data);
    }

    public void Resize(ResourceKey resource, int cols, int rows)
    {
        ConsoleLiveSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        session?.Resize(cols, rows);
    }

    public void EndSession(ResourceKey resource)
    {
        ConsoleLiveSession? session;
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(resource, out session))
            {
                _sessions.Remove(resource);
            }
        }

        if (session is not null)
        {
            ReleaseSession(session);
        }
    }

    public bool TryBindConnection(Guid sessionToken, int connectionId)
    {
        Guid boundSessionId;
        lock (_sessionsLock)
        {
            ConsoleLiveSession? match = null;
            foreach (var session in _sessions.Values)
            {
                if (session.SessionId == sessionToken)
                {
                    match = session;
                    break;
                }
            }

            if (match is null)
            {
                return false;
            }

            match.ConnectionId = connectionId;
            match.HasConnected = true;
            _connectionToSession[connectionId] = sessionToken;
            boundSessionId = match.SessionId;
        }

        var connectedMessage = new ConsoleSessionConnectedMessage(boundSessionId);
        _messengerService.Send(connectedMessage);

        return true;
    }

    public void OnConnectionLost(int connectionId)
    {
        // Attribution only: the session's state follows its pty, so losing a client (for instance a nested
        // celbridge-py exiting back to the shell prompt) leaves the session running with stale runners.
        lock (_sessionsLock)
        {
            if (!_connectionToSession.Remove(connectionId, out var sessionId))
            {
                return;
            }

            foreach (var session in _sessions.Values)
            {
                if (session.SessionId == sessionId)
                {
                    session.ConnectionId = null;
                    return;
                }
            }
        }
    }

    public IReadOnlyList<ConsoleRunTarget> GetRunTargets(string fileExtension)
    {
        var runningSessions = new List<ConsoleLiveSession>();
        lock (_sessionsLock)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.State != ConsoleSessionRunState.Running)
                {
                    continue;
                }

                runningSessions.Add(session);
            }
        }

        // Resolving is in-memory once the resolver has verified a folder and the key is in the registry
        // tree, which holds for an open console. A cold resolve can still reach disk though, so it runs
        // outside the lock that inbound transport callbacks also take.
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var candidates = new List<ConsoleRunCandidate>();
        foreach (var session in runningSessions)
        {
            // Best effort: a path that will not resolve still gets a menu entry, just one that cannot be
            // qualified by its folder if another console shares its file name.
            var filePath = session.Resource.ResourceName;
            var resolveResult = registry.ResolveResourcePath(session.Resource);
            if (resolveResult.IsSuccess)
            {
                filePath = resolveResult.Value;
            }

            var candidate = new ConsoleRunCandidate(
                session.SessionId,
                session.Resource,
                filePath,
                session.Runners,
                session.HasStaleRunners);

            candidates.Add(candidate);
        }

        return ConsoleRunTargets.Resolve(candidates, fileExtension);
    }

    public void RunScript(Guid sessionId, string scriptPath, string arguments)
    {
        ConsoleLiveSession? target = null;
        lock (_sessionsLock)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.SessionId == sessionId)
                {
                    target = session;
                    break;
                }
            }
        }

        if (target is null)
        {
            _logger.LogWarning("No console session {SessionId} to run '{Script}'", sessionId, scriptPath);
            return;
        }

        if (target.HasStaleRunners)
        {
            _logger.LogWarning("Console '{Resource}' lost its client connection; not injecting a run command", target.Resource);
            return;
        }

        var extension = Path.GetExtension(scriptPath);
        var runner = ConsoleRunTargets.FindRunner(target.Runners, extension);
        if (runner is null)
        {
            _logger.LogWarning("No runner for '{Extension}' in console '{Resource}'", extension, target.Resource);
            return;
        }

        var command = runner.CommandTemplate.Replace("{script_path}", scriptPath);
        if (!string.IsNullOrEmpty(arguments))
        {
            command += " " + arguments;
        }

        target.InjectCommand(command);
    }

    private void OnSessionStateChanged(object? sender, ConsoleSessionRunState state)
    {
        if (sender is not ConsoleLiveSession session)
        {
            return;
        }

        var stateChangedMessage = new ConsoleSessionStateChangedMessage(session.SessionId, state);
        _messengerService.Send(stateChangedMessage);
    }

    // A released session is terminally gone, so its state change is broadcast for per-session bookkeeping
    // (a pending Python fingerprint, for instance) before its handler is detached.
    private void ReleaseSession(ConsoleLiveSession session)
    {
        if (session.State != ConsoleSessionRunState.Ended &&
            session.State != ConsoleSessionRunState.Failed)
        {
            var endedMessage = new ConsoleSessionStateChangedMessage(session.SessionId, ConsoleSessionRunState.Ended);
            _messengerService.Send(endedMessage);
        }

        session.StateChanged -= OnSessionStateChanged;

        lock (_sessionsLock)
        {
            if (session.ConnectionId is int connectionId)
            {
                _connectionToSession.Remove(connectionId);
            }
        }

        session.Dispose();
    }

    private void OnDocumentOpened(object recipient, DocumentOpenedMessage message)
    {
        if (!IsConsoleResource(message.DocumentResource))
        {
            return;
        }

        _ = EnsureStartedAsync(message.DocumentResource);
    }

    private void OnDocumentClosed(object recipient, DocumentClosedMessage message)
    {
        if (!IsConsoleResource(message.DocumentResource))
        {
            return;
        }

        EndSession(message.DocumentResource);
    }

    private void OnDocumentResourceChanged(object recipient, DocumentResourceChangedMessage message)
    {
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(message.OldResource, out var session))
            {
                _sessions.Remove(message.OldResource);
                _sessions[message.NewResource] = session;
                session.Rekey(message.NewResource);
            }
        }
    }

    private static bool IsConsoleResource(ResourceKey resource)
    {
        return resource.ResourceName.EndsWith(ConsoleFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        List<ConsoleLiveSession> sessions;
        lock (_sessionsLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }

        _messengerService.UnregisterAll(this);

        foreach (var session in sessions)
        {
            session.StateChanged -= OnSessionStateChanged;
            session.Dispose();
        }

        _proxyListener.Dispose();
    }
}
