using System.Net;
using System.Net.Sockets;
using Celbridge.Logging;
using Celbridge.Server;

namespace Celbridge.Console.Services;

/// <summary>
/// The shared cel-proxy JSON-RPC listener every console's clients dial back on. One loopback listener
/// multiplexes all peers: each connection gets its own handshake target so it can attribute itself to the
/// session that launched it. Started lazily by the first session launch.
/// </summary>
internal sealed class ConsoleProxyListener : IDisposable
{
    private readonly ITcpTransport _tcpTransport;
    private readonly IConsoleSessionService _sessionService;
    private readonly ILogger<ConsoleProxyListener> _logger;

    private readonly object _listenerLock = new();
    private CancellationTokenSource? _listenerCancellation;
    private int _port;
    private bool _started;
    private bool _disposed;

    public ConsoleProxyListener(
        ITcpTransport tcpTransport,
        IConsoleSessionService sessionService,
        ILogger<ConsoleProxyListener> logger)
    {
        _tcpTransport = tcpTransport;
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the listener is running and returns its loopback port.
    /// </summary>
    public int EnsureStarted()
    {
        lock (_listenerLock)
        {
            if (_started)
            {
                return _port;
            }

            _port = GetAvailableTcpPort();

            // Bind one handshake target per connection so the handshake can attribute the connection, and
            // follow lost connections to stale the owning session's runners. Both must be wired before
            // listening starts.
            _tcpTransport.AddRpcTargetFactory(connectionId => new SessionHandshakeHandler(_sessionService, connectionId));
            _tcpTransport.ConnectionLost += OnConnectionLost;

            _listenerCancellation = new CancellationTokenSource();
            var cancellationToken = _listenerCancellation.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _tcpTransport.StartListeningAsync(_port, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Workspace teardown.
                }
                catch (Exception exception)
                {
                    // The port could have been taken between probing and binding. Clients cannot connect
                    // until the workspace reloads, so fail loud in the log.
                    _logger.LogError(exception, "The console cel-proxy listener failed to start on port {Port}", _port);
                }
            });

            _started = true;
            _logger.LogInformation("Console cel-proxy listener started on port {Port}", _port);

            return _port;
        }
    }

    private void OnConnectionLost(int connectionId)
    {
        _sessionService.OnConnectionLost(connectionId);
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

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
