using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using StreamJsonRpc;

namespace Celbridge.Server.Services;

/// <summary>
/// TCP transport for the agent server. Listens on localhost for JSON-RPC
/// connections and exposes tool methods (tools/list, tools/call) plus any
/// additional RPC targets to all connected clients.
/// </summary>
public class TcpTransport : ITcpTransport
{
    private readonly ILogger<TcpTransport> _logger;
    private readonly IMcpToolBridge _mcpToolBridge;
    private readonly List<object> _additionalTargets = new();

    private TcpListener? _listener;
    private int _nextConnectionId;
    private readonly ConcurrentDictionary<int, ClientConnection> _activeConnections = new();
    private readonly ConcurrentBag<Task> _monitorTasks = new();

    private volatile bool _disposed;

    private record class ClientConnection(TcpClient TcpClient, JsonRpc JsonRpc);

    public int ActiveConnectionCount => _activeConnections.Count;

    public event Action<int>? ConnectionAccepted;
    public event Action<int>? ConnectionLost;

    public TcpTransport(
        ILogger<TcpTransport> logger,
        IMcpToolBridge mcpToolBridge)
    {
        _logger = logger;
        _mcpToolBridge = mcpToolBridge;
    }

    /// <summary>
    /// Registers an additional RPC target whose public methods will be exposed
    /// to all connections. Must be called before StartListeningAsync.
    /// </summary>
    public void AddRpcTarget(object target)
    {
        _additionalTargets.Add(target);
    }

    public async Task StartListeningAsync(int port, CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _logger.LogInformation("Agent server TCP transport listening on port {Port}", port);

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
            _logger.LogInformation("Agent server TCP transport stopped listening on port {Port}", port);
        }
    }

    private async Task MonitorConnectionAsync(int connectionId, TcpClient tcpClient)
    {
        var networkStream = tcpClient.GetStream();
        var jsonRpc = new JsonRpc(networkStream, networkStream);

        // Suppress StreamJsonRpc's built-in TraceSource logging for expected
        // disconnections (e.g. IOException when a terminal window is closed).
        jsonRpc.TraceSource = new TraceSource("JsonRpc", SourceLevels.Off);

        var targetOptions = new JsonRpcTargetOptions
        {
            MethodNameTransform = name => name
        };

        // Register the MCP tool bridge (tools/list, tools/call).
        // The [JsonRpcMethod] attributes on McpToolBridge override the
        // method name transform, so slashed names work correctly.
        jsonRpc.AddLocalRpcTarget(_mcpToolBridge, targetOptions);

        // Register any additional targets (e.g. PythonRpcHandler)
        foreach (var target in _additionalTargets)
        {
            jsonRpc.AddLocalRpcTarget(target, targetOptions);
        }

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

    ~TcpTransport()
    {
        Dispose(false);
    }
}
