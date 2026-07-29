using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Server;

namespace Celbridge.Console.Services;

/// <summary>
/// The live state of one open console: its registration (identity, runners, injector) plus its current
/// session id, state, and bound connection. RegistrationOrder is the console's first-open sequence,
/// preserved across a reopen so Run targets keep a stable order. HasConnected records that a client bound
/// at some point this session, so a lost binding can be told apart from a type that never binds.
/// </summary>
internal sealed class OpenConsole
{
    public required ConsoleRegistration Registration { get; set; }
    public Guid SessionId { get; set; }
    public ConsoleSessionState State { get; set; }
    public int? ConnectionId { get; set; }
    public bool HasConnected { get; set; }
    public long RegistrationOrder { get; set; }

    // A console that bound a client connection and then lost it is a live shell whose REPL exited; its
    // runners target the REPL, so they are stale until a client reconnects (or the console reopens).
    public bool HasStaleRunners => HasConnected && ConnectionId is null;

    public ConsoleSession ToSession()
    {
        return new ConsoleSession(
            SessionId,
            Registration.ResourceKey,
            Registration.TypeId,
            Registration.Title,
            State,
            ConnectionId);
    }
}

/// <summary>
/// Workspace-scoped registry of open consoles. It owns the shared cel-proxy JSON-RPC listener (started
/// lazily on the first host-bound console), maps inbound connections to the console that launched them via
/// the session/handshake handshake, and resolves the Explorer Run menu's targets from the open consoles.
/// </summary>
public sealed class ConsoleSessionRegistry : IConsoleSessionRegistry, IDisposable
{
    private readonly ITcpTransport _tcpTransport;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<ConsoleSessionRegistry> _logger;

    // Keyed by the console resource: one open console per .console file. The session id is regenerated on
    // each launch (reopen), so the connection map keys by connection id back to the session id.
    private readonly ConcurrentDictionary<ResourceKey, OpenConsole> _consoles = new();
    private readonly ConcurrentDictionary<int, Guid> _connectionToSession = new();

    private readonly object _listenerLock = new();
    private CancellationTokenSource? _listenerCancellation;
    private int _rpcPort;
    private bool _listenerStarted;
    private long _nextRegistrationOrder;

    public ConsoleSessionRegistry(
        ITcpTransport tcpTransport,
        IMessengerService messengerService,
        ILogger<ConsoleSessionRegistry> logger)
    {
        _tcpTransport = tcpTransport;
        _messengerService = messengerService;
        _logger = logger;
    }

    public async Task<int> EnsureRpcListenerAsync()
    {
        await Task.CompletedTask;

        lock (_listenerLock)
        {
            if (_listenerStarted)
            {
                return _rpcPort;
            }

            _rpcPort = GetAvailableTcpPort();

            // Bind one session/handshake target per connection so the handshake can attribute the connection,
            // and follow lost connections to fail the owning console. Both must be wired before listening.
            _tcpTransport.AddRpcTargetFactory(connectionId => new SessionHandshakeHandler(this, connectionId));
            _tcpTransport.ConnectionLost += OnConnectionLost;

            _listenerCancellation = new CancellationTokenSource();
            var cancellationToken = _listenerCancellation.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _tcpTransport.StartListeningAsync(_rpcPort, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Workspace teardown.
                }
                catch (Exception exception)
                {
                    // The port could have been taken between probing and binding. Host-bound consoles
                    // cannot connect until the workspace reloads, so fail loud in the log.
                    _logger.LogError(exception, "The console cel-proxy listener failed to start on port {Port}", _rpcPort);
                }
            });

            _listenerStarted = true;
            _logger.LogInformation("Console cel-proxy listener started on port {Port}", _rpcPort);

            return _rpcPort;
        }
    }

    public ConsoleSession Register(ConsoleRegistration registration)
    {
        var sessionId = Guid.NewGuid();

        // A reopen keeps the console's first-open order. The replaced session is terminally gone, so
        // broadcast its end for per-session bookkeeping (e.g. pending fingerprints).
        var registrationOrder = Interlocked.Increment(ref _nextRegistrationOrder);
        if (_consoles.TryGetValue(registration.ResourceKey, out var existingConsole))
        {
            registrationOrder = existingConsole.RegistrationOrder;
            if (existingConsole.State != ConsoleSessionState.Ended)
            {
                existingConsole.State = ConsoleSessionState.Ended;
                BroadcastState(existingConsole);
            }
        }

        var openConsole = new OpenConsole
        {
            Registration = registration,
            SessionId = sessionId,
            State = ConsoleSessionState.Ready,
            ConnectionId = null,
            RegistrationOrder = registrationOrder,
        };

        _consoles[registration.ResourceKey] = openConsole;

        BroadcastState(openConsole);
        return openConsole.ToSession();
    }

    public void SetState(ResourceKey resourceKey, ConsoleSessionState state)
    {
        if (!_consoles.TryGetValue(resourceKey, out var openConsole))
        {
            return;
        }

        openConsole.State = state;
        BroadcastState(openConsole);
    }

    public void Unregister(ResourceKey resourceKey)
    {
        if (!_consoles.TryRemove(resourceKey, out var openConsole))
        {
            return;
        }

        if (openConsole.ConnectionId is int connectionId)
        {
            _connectionToSession.TryRemove(connectionId, out _);
        }

        // The removed session is terminally gone; broadcast its end for per-session bookkeeping.
        if (openConsole.State != ConsoleSessionState.Ended)
        {
            openConsole.State = ConsoleSessionState.Ended;
            BroadcastState(openConsole);
        }
    }

    public bool TryBindConnection(Guid sessionToken, int connectionId, out ConsoleSession? session)
    {
        foreach (var openConsole in _consoles.Values)
        {
            if (openConsole.SessionId != sessionToken)
            {
                continue;
            }

            openConsole.ConnectionId = connectionId;
            openConsole.HasConnected = true;
            _connectionToSession[connectionId] = sessionToken;

            _messengerService.Send(new ConsoleSessionConnectedMessage(openConsole.SessionId));
            session = openConsole.ToSession();
            return true;
        }

        session = null;
        return false;
    }

    public void OnConnectionLost(int connectionId)
    {
        if (!_connectionToSession.TryRemove(connectionId, out var sessionId))
        {
            return;
        }

        // Attribution only: the console's state follows its pty, so losing a client connection (e.g.
        // exiting a nested celbridge-py back to a shell prompt) changes nothing else.
        foreach (var openConsole in _consoles.Values)
        {
            if (openConsole.SessionId != sessionId)
            {
                continue;
            }

            openConsole.ConnectionId = null;
            return;
        }
    }

    public bool TryGetByResource(ResourceKey resourceKey, out ConsoleSession? session)
    {
        if (_consoles.TryGetValue(resourceKey, out var openConsole))
        {
            session = openConsole.ToSession();
            return true;
        }

        session = null;
        return false;
    }

    public IReadOnlyList<ConsoleRunTarget> GetRunTargets(string fileExtension)
    {
        var candidates = new List<OpenConsole>();
        foreach (var openConsole in _consoles.Values)
        {
            if (openConsole.State != ConsoleSessionState.Ready)
            {
                continue;
            }

            if (openConsole.HasStaleRunners)
            {
                continue;
            }

            var runner = FindRunner(openConsole.Registration.Runners, fileExtension);
            if (runner is null)
            {
                continue;
            }

            candidates.Add(openConsole);
        }

        // First-open order, so the menu and the no-session programmatic fallback are deterministic.
        var ordered = candidates
            .OrderBy(candidate => candidate.RegistrationOrder)
            .ToList();

        var targets = new List<ConsoleRunTarget>();
        foreach (var openConsole in ordered)
        {
            var displayName = string.IsNullOrWhiteSpace(openConsole.Registration.Title)
                ? openConsole.Registration.ResourceKey.ResourceName
                : openConsole.Registration.Title;

            var target = new ConsoleRunTarget(
                openConsole.SessionId,
                openConsole.Registration.ResourceKey,
                displayName);

            targets.Add(target);
        }

        return targets;
    }

    public void RunScript(Guid sessionId, string scriptPath, string arguments)
    {
        foreach (var openConsole in _consoles.Values)
        {
            if (openConsole.SessionId != sessionId)
            {
                continue;
            }

            if (openConsole.HasStaleRunners)
            {
                _logger.LogWarning("Console '{Resource}' lost its client connection; not injecting a run command", openConsole.Registration.ResourceKey);
                return;
            }

            var extension = Path.GetExtension(scriptPath);
            var runner = FindRunner(openConsole.Registration.Runners, extension);
            if (runner is null)
            {
                _logger.LogWarning("No runner for '{Extension}' in console '{Resource}'", extension, openConsole.Registration.ResourceKey);
                return;
            }

            var command = runner.CommandTemplate.Replace("{script_path}", scriptPath);
            if (!string.IsNullOrEmpty(arguments))
            {
                command += " " + arguments;
            }

            openConsole.Registration.Injector.InjectCommand(command);
            return;
        }
    }

    private static ConsoleRunner? FindRunner(IReadOnlyList<ConsoleRunner> runners, string fileExtension)
    {
        foreach (var runner in runners)
        {
            foreach (var extension in runner.FileExtensions)
            {
                if (string.Equals(extension, fileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return runner;
                }
            }
        }

        return null;
    }

    private void BroadcastState(OpenConsole openConsole)
    {
        _messengerService.Send(new ConsoleSessionStateChangedMessage(openConsole.SessionId, openConsole.State));
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _tcpTransport.ConnectionLost -= OnConnectionLost;

        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();
        _listenerCancellation = null;

        _tcpTransport.Dispose();
    }
}
