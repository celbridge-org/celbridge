using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Celbridge.Python.Services;

/// <summary>
/// Manages JSON-RPC communication with the Python connector over TCP.
/// Acts as a TCP server that accepts multiple concurrent connections, each
/// independently dispatching JSON-RPC calls to the registered handler.
/// Each connection is assigned a unique incrementing ID for logging and routing.
/// </summary>
public class RpcService : IRpcService
{
    private readonly ILogger<RpcService> _logger;
    private readonly PythonRpcHandler _handler;

    private TcpListener? _listener;
    private int _nextConnectionId;
    private readonly ConcurrentDictionary<int, ClientConnection> _activeConnections = new();
    private readonly ConcurrentBag<Task> _monitorTasks = new();

    private volatile bool _disposed;

    private record class ClientConnection(TcpClient TcpClient, JsonRpc JsonRpc);

    public int ActiveConnectionCount => _activeConnections.Count;

    public event Action<int>? ConnectionAccepted;
    public event Action<int>? ConnectionLost;

    public RpcService(ILogger<RpcService> logger, PythonRpcHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    public async Task StartListeningAsync(int port, CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _logger.LogInformation("RPC server listening on port {Port}", port);

        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                    var connectionId = Interlocked.Increment(ref _nextConnectionId);
                    _logger.LogInformation("Connection {ConnectionId} established", connectionId);

                    var monitorTask = MonitorConnectionAsync(connectionId, tcpClient);
                    _monitorTasks.Add(monitorTask);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (_disposed)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!_disposed && !cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Error in connection accept loop, retrying");
                        await Task.Delay(500, cancellationToken);
                    }
                }
            }
        }
        finally
        {
            StopListener();
            _logger.LogInformation("RPC server stopped listening on port {Port}", port);
        }
    }

    private async Task MonitorConnectionAsync(int connectionId, TcpClient tcpClient)
    {
        var networkStream = tcpClient.GetStream();
        var jsonRpc = new JsonRpc(networkStream, networkStream);

        // Suppress StreamJsonRpc's built-in TraceSource logging for expected
        // disconnections (e.g. IOException when a terminal window is closed).
        // We handle disconnect logging ourselves via the Disconnected event.
        jsonRpc.TraceSource = new TraceSource("JsonRpc", SourceLevels.Off);

        jsonRpc.AddLocalRpcTarget(_handler, new JsonRpcTargetOptions
        {
            MethodNameTransform = name => name
        });

        var disconnectionSource = new TaskCompletionSource();

        jsonRpc.Disconnected += (sender, eventArgs) =>
        {
            _logger.LogInformation("Connection {ConnectionId} disconnected", connectionId);
            disconnectionSource.TrySetResult();
        };

        var clientConnection = new ClientConnection(tcpClient, jsonRpc);
        _activeConnections[connectionId] = clientConnection;

        jsonRpc.StartListening();
        ConnectionAccepted?.Invoke(connectionId);

        await disconnectionSource.Task;

        _activeConnections.TryRemove(connectionId, out _);

        // Observe the Completion task to prevent UnobservedTaskException.
        // StreamJsonRpc's internal read loop may fault with IOException when
        // a client closes the connection, and that exception must be observed.
        try
        {
            await jsonRpc.Completion;
        }
        catch (IOException)
        {
            // Expected when a terminal window is closed
        }
        catch (ConnectionLostException)
        {
            // Expected when the remote end disconnects
        }

        jsonRpc.Dispose();
        tcpClient.Dispose();
        ConnectionLost?.Invoke(connectionId);
    }

    private void StopListener()
    {
        if (_listener != null)
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Already stopped
            }
            _listener = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;

            if (disposing)
            {
                StopListener();

                foreach (var pair in _activeConnections)
                {
                    pair.Value.JsonRpc.Dispose();
                    pair.Value.TcpClient.Dispose();
                }
                _activeConnections.Clear();

                try
                {
                    Task.WaitAll(_monitorTasks.ToArray());
                }
                catch (AggregateException)
                {
                    // Monitor tasks may throw on cancellation during shutdown
                }
            }
        }
    }

    ~RpcService()
    {
        Dispose(false);
    }
}
