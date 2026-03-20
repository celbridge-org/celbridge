using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Celbridge.Python.Services;

/// <summary>
/// Manages JSON-RPC communication with the Python connector over TCP.
/// Acts as a TCP server that accepts connections in a loop, supporting
/// reconnection when a client disconnects and a new one connects.
/// Each connection is assigned a unique incrementing ID for logging and routing.
/// </summary>
public class RpcService : IRpcService
{
    private readonly ILogger<RpcService> _logger;
    private readonly PythonRpcHandler _handler;

    private TcpListener? _listener;
    private TcpClient? _tcpClient;
    private JsonRpc? _rpc;
    private int _nextConnectionId;

    private volatile bool _disposed;

    public bool IsConnected
    {
        get
        {
            var rpc = _rpc;
            return rpc != null && !rpc.IsDisposed;
        }
    }

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
                    // Wait for a Python connector to connect
                    _tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);

                    var connectionId = ++_nextConnectionId;
                    _logger.LogInformation("Connection {ConnectionId} established", connectionId);

                    // Create JsonRpc instance over the TCP stream.
                    // Preserve exact method names so Python can call C# methods by their
                    // PascalCase names (e.g., "GetAppVersion", "Log").
                    var networkStream = _tcpClient.GetStream();
                    _rpc = new JsonRpc(networkStream, networkStream);
                    _rpc.AddLocalRpcTarget(_handler, new JsonRpcTargetOptions
                    {
                        MethodNameTransform = name => name
                    });

                    // Use a TaskCompletionSource to wait for disconnection before accepting
                    // the next connection. This keeps the accept loop sequential.
                    var disconnectionSource = new TaskCompletionSource();

                    _rpc.Disconnected += (sender, eventArgs) =>
                    {
                        if (eventArgs.Exception != null)
                        {
                            _logger.LogWarning(eventArgs.Exception, "Connection {ConnectionId} disconnected unexpectedly", connectionId);
                        }
                        else
                        {
                            _logger.LogInformation("Connection {ConnectionId} disconnected", connectionId);
                        }

                        CleanupConnection();
                        ConnectionLost?.Invoke(connectionId);
                        disconnectionSource.TrySetResult();
                    };

                    _rpc.StartListening();
                    ConnectionAccepted?.Invoke(connectionId);

                    // Wait for this connection to disconnect before accepting the next one
                    await disconnectionSource.Task.WaitAsync(cancellationToken);
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
                        CleanupConnection();
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

    private void CleanupConnection()
    {
        if (_rpc != null)
        {
            _rpc.Dispose();
            _rpc = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;
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
                CleanupConnection();
                StopListener();
            }
        }
    }

    ~RpcService()
    {
        Dispose(false);
    }
}
