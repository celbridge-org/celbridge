using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Server;

namespace Celbridge.Console.Services;

/// <summary>
/// The live state of one open console: its registration (identity, runners, injector) plus its current
/// session id, state, and bound connection.
/// </summary>
internal sealed class OpenConsole
{
    public required ConsoleRegistration Registration { get; set; }
    public Guid SessionId { get; set; }
    public ConsoleSessionState State { get; set; }
    public int? ConnectionId { get; set; }

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
            _ = Task.Run(() => _tcpTransport.StartListeningAsync(_rpcPort, cancellationToken));

            _listenerStarted = true;
            _logger.LogInformation("Console cel-proxy listener started on port {Port}", _rpcPort);

            return _rpcPort;
        }
    }

    public ConsoleSession Register(ConsoleRegistration registration)
    {
        var sessionId = Guid.NewGuid();

        // A plain shell is a runnable target as soon as its pty is up. A host-bound console must wait for
        // its client to say hello before it counts as Ready.
        var initialState = registration.HostBinding == ConsoleHostBinding.None
            ? ConsoleSessionState.Ready
            : ConsoleSessionState.Connecting;

        var openConsole = new OpenConsole
        {
            Registration = registration,
            SessionId = sessionId,
            State = initialState,
            ConnectionId = null,
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
            openConsole.State = ConsoleSessionState.Ready;
            _connectionToSession[connectionId] = sessionToken;

            BroadcastState(openConsole);
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

        foreach (var openConsole in _consoles.Values)
        {
            if (openConsole.SessionId != sessionId)
            {
                continue;
            }

            openConsole.ConnectionId = null;
            openConsole.State = ConsoleSessionState.Disconnected;
            BroadcastState(openConsole);
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
        var targets = new List<ConsoleRunTarget>();

        foreach (var openConsole in _consoles.Values)
        {
            if (openConsole.State != ConsoleSessionState.Ready)
            {
                continue;
            }

            var runner = FindRunner(openConsole.Registration.Runners, fileExtension);
            if (runner is null)
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(openConsole.Registration.Title)
                ? openConsole.Registration.ResourceKey.ResourceName
                : openConsole.Registration.Title;

            var target = new ConsoleRunTarget(
                openConsole.SessionId,
                openConsole.Registration.ResourceKey,
                displayName,
                runner.CommandTemplate);

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
