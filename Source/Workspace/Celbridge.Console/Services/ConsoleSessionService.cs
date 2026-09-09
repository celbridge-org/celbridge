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

    // The size a console falls back to when no view reports one in time. A console whose document is open
    // but whose view never arranges still has to run.
    private const int DefaultCols = 120;
    private const int DefaultRows = 30;

    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<ConsoleSessionService> _logger;

    private readonly object _sessionsLock = new();
    private readonly Dictionary<ResourceKey, ConsoleSession> _sessions = new();

    // Maps a bound transport connection to the session that launched it. Only sessions whose client has
    // connected appear here.
    private readonly Dictionary<int, Guid> _connectionToSession = new();

    private readonly ConsoleProxyListener _proxyListener;
    private readonly ConsoleTriggerScheduler _triggerScheduler;

    // Collected once: the registered providers are fixed for the process, and every session resolves its
    // provider and parses its document against the same set.
    private readonly IReadOnlyList<IConsoleSessionProvider> _sessionProviders;
    private readonly IReadOnlyList<ConsoleSessionType> _sessionTypes;

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

        _sessionProviders = ResolveSessionProviders(serviceProvider);
        _sessionTypes = _sessionProviders.Select(provider => provider.SessionType).ToList();

        _triggerScheduler = new ConsoleTriggerScheduler(FireTrigger);

        var tcpTransport = serviceProvider.GetRequiredService<ITcpTransport>();
        var listenerLogger = serviceProvider.GetRequiredService<ILogger<ConsoleProxyListener>>();
        _proxyListener = new ConsoleProxyListener(tcpTransport, this, listenerLogger);

        _messengerService.Register<DocumentOpenedMessage>(this, OnDocumentOpened);
        _messengerService.Register<DocumentClosedMessage>(this, OnDocumentClosed);
        _messengerService.Register<DocumentResourceChangedMessage>(this, OnDocumentResourceChanged);
        _messengerService.Register<ResourceChangedMessage>(this, OnResourceChanged);
        _messengerService.Register<ResourceCreatedMessage>(this, OnResourceCreated);
    }

    public bool HasRunningSessions
    {
        get
        {
            lock (_sessionsLock)
            {
                foreach (var session in _sessions.Values)
                {
                    if (session.State == ConsoleSessionRunState.Running)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public async Task EnsureStartedAsync(ResourceKey resource)
    {
        var session = GetOrCreateSession(resource);
        if (session is null)
        {
            return;
        }

        await StartSessionAsync(session, resource);
    }

    // The session object for a console, created if it has none yet. Separate from the launch because a launch
    // waits for a view's size, so a caller reports that size through the session before awaiting the launch.
    private ConsoleSession? GetOrCreateSession(ResourceKey resource)
    {
        lock (_sessionsLock)
        {
            if (_disposed)
            {
                return null;
            }

            if (!_sessions.TryGetValue(resource, out var session))
            {
                session = new ConsoleSession(_serviceProvider, _workspaceWrapper, resource, _sessionProviders);
                session.StateChanged += OnSessionStateChanged;
                _sessions[resource] = session;
            }

            return session;
        }
    }

    // Launches the session if it has not been launched already, and waits for that launch.
    private async Task StartSessionAsync(ConsoleSession session, ResourceKey resource)
    {
        // Every console shares one cel-proxy listener, so the first launch starts it and the rest reuse
        // the port.
        var rpcPort = _proxyListener.EnsureStarted();

        lock (_sessionsLock)
        {
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
        var session = GetOrCreateSession(resource);
        if (session is null)
        {
            return UnavailableSnapshot();
        }

        // Reported before the launch is awaited. The launch holds the pty until a view reports a size, so
        // awaiting it first would leave the launch and the attach waiting on each other.
        session.Resize(cols, rows);

        await StartSessionAsync(session, resource);

        // Applied again now the pty exists. A launch that timed out waiting created it at the fallback size.
        session.Resize(cols, rows);

        return session.Attach(attachedView);
    }

    private static ConsoleAttachSnapshot UnavailableSnapshot()
    {
        return new ConsoleAttachSnapshot(
            ConsoleSessionRunState.Failed,
            "The console session is not available.",
            false,
            string.Empty,
            null);
    }

    public void Detach(ResourceKey resource, IConsoleView attachedView)
    {
        ConsoleSession? session;
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
            var session = GetOrCreateSession(resource);
            if (session is null)
            {
                return UnavailableSnapshot();
            }

            session.Resize(cols, rows);

            await StartSessionAsync(session, resource);

            session.Resize(cols, rows);

            return new ConsoleAttachSnapshot(session.State, session.Error, false, string.Empty, session.LaunchedConfigToml);
        }

        return await AttachAsync(resource, previousView, cols, rows);
    }

    public void Input(ResourceKey resource, string data)
    {
        ConsoleSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        session?.Input(data);
    }

    public void Resize(ResourceKey resource, int cols, int rows)
    {
        ConsoleSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        session?.Resize(cols, rows);
    }

    public void EndSession(ResourceKey resource)
    {
        ConsoleSession? session;
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
            ConsoleSession? match = null;
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

    public IReadOnlyList<ConsoleSessionType> GetSessionTypes()
    {
        return _sessionTypes;
    }

    // A session type whose id a .console file cannot name would have its table read as something else, so
    // the registered set is checked as the service is built rather than once a document depends on it.
    private static IReadOnlyList<IConsoleSessionProvider> ResolveSessionProviders(IServiceProvider serviceProvider)
    {
        var sessionProviders = serviceProvider.GetServices<IConsoleSessionProvider>().ToList();
        var sessionTypes = sessionProviders.Select(provider => provider.SessionType).ToList();

        var validateResult = ConsoleSessionTypeValidator.Validate(sessionTypes);
        if (validateResult.IsFailure)
        {
            throw new InvalidOperationException(validateResult.FirstErrorMessage);
        }

        return sessionProviders;
    }

    public IReadOnlyList<ConsoleRunTarget> GetRunTargets(string fileExtension)
    {
        var runningSessions = new List<ConsoleSession>();
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

    public Result<string> ResolveRunnerInvocation(Guid sessionId, string scriptPath, string arguments)
    {
        var findResult = FindSubmittableSession(sessionId);
        if (findResult.IsFailure)
        {
            return Result<string>.Fail($"Cannot resolve a runner for '{scriptPath}'")
                .WithErrors(findResult);
        }
        var session = findResult.Value;

        var extension = Path.GetExtension(scriptPath);
        var runner = ConsoleRunTargets.FindRunner(session.Runners, extension);
        if (runner is null)
        {
            return Result<string>.Fail($"Console '{session.Resource}' has no runner for '{extension}'");
        }

        var invocation = ConsoleInvocationTemplate.Substitute(runner.Command, scriptPath);
        if (!string.IsNullOrEmpty(arguments))
        {
            invocation += " " + arguments;
        }

        return invocation;
    }

    public Result SubmitInvocation(Guid sessionId, string invocation)
    {
        var findResult = FindSubmittableSession(sessionId);
        if (findResult.IsFailure)
        {
            return Result.Fail("Cannot submit an invocation")
                .WithErrors(findResult);
        }
        var session = findResult.Value;

        session.InjectInvocation(invocation);

        return Result.Ok();
    }

    public void SubmitInvocation(ResourceKey resource, string invocation)
    {
        ConsoleSession? session;
        lock (_sessionsLock)
        {
            _sessions.TryGetValue(resource, out session);
        }

        session?.InjectInvocation(invocation);
    }

    // A session that can still accept an invocation. A session that bound a client and then lost it is a
    // live shell whose REPL has exited, so its prompt is no longer the one the invocation was written for.
    private Result<ConsoleSession> FindSubmittableSession(Guid sessionId)
    {
        ConsoleSession? target = null;
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
            return Result<ConsoleSession>.Fail($"No console session {sessionId}");
        }

        if (target.HasStaleRunners)
        {
            return Result<ConsoleSession>.Fail($"Console '{target.Resource}' has lost its client connection");
        }

        return target;
    }

    private void OnSessionStateChanged(object? sender, ConsoleSessionRunState state)
    {
        if (sender is not ConsoleSession session)
        {
            return;
        }

        var stateChangedMessage = new ConsoleSessionStateChangedMessage(session.SessionId, state);
        _messengerService.Send(stateChangedMessage);
    }

    // A released session is terminally gone, so its state change is broadcast for per-session bookkeeping
    // (a pending Python fingerprint, for instance) before its handler is detached.
    private void ReleaseSession(ConsoleSession session)
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

    private void OnResourceChanged(object recipient, ResourceChangedMessage message)
    {
        ScheduleTriggers(message.Resource);
    }

    private void OnResourceCreated(object recipient, ResourceCreatedMessage message)
    {
        ScheduleTriggers(message.Resource);
    }

    // The watcher reports one write as several events (an atomic save arrives as both Created and Changed),
    // and an editor saves on a timer while the user types. The scheduler's window is what turns all of that
    // into a single run, so every match is scheduled here and the collapsing happens there.
    private void ScheduleTriggers(ResourceKey resource)
    {
        List<ConsoleSession> sessions;
        lock (_sessionsLock)
        {
            if (_disposed)
            {
                return;
            }

            sessions = _sessions.Values.ToList();
        }

        foreach (var session in sessions)
        {
            if (session.Triggers.Count == 0)
            {
                continue;
            }

            var invocations = ConsoleTriggerMatcher.Resolve(session.Triggers, resource);
            foreach (var invocation in invocations)
            {
                _triggerScheduler.Schedule(session.SessionId, invocation);
            }
        }
    }

    // Submits straight to the session rather than through a command. A trigger firing is the tail of a
    // decision the host has already made, not a capability worth exposing to automation, and the command
    // queue stalls behind any command that awaits a modal dialog, which would hold the run back and then
    // release a burst of them. Runs on the scheduler's background task, so a fault here has nothing above
    // it to observe and is logged rather than left to escape.
    private void FireTrigger(Guid sessionId, string invocation)
    {
        try
        {
            var submitResult = SubmitInvocation(sessionId, invocation);
            if (submitResult.IsFailure)
            {
                // A console can close or reopen while a trigger's debounce is running, leaving nothing to
                // submit to. That is ordinary for an automatic run rather than a fault.
                _logger.LogDebug("Console trigger did not run '{Invocation}': {Reason}",
                    invocation, submitResult.FirstErrorMessage);

                return;
            }

            _logger.LogDebug("Trigger ran '{Invocation}' in console session {SessionId}", invocation, sessionId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to run a console trigger");
        }
    }

    private static bool IsConsoleResource(ResourceKey resource)
    {
        return resource.ResourceName.EndsWith(ConsoleFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        List<ConsoleSession> sessions;
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
