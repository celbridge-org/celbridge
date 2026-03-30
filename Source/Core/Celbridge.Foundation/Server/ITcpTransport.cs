namespace Celbridge.Server;

/// <summary>
/// A TCP transport that accepts JSON-RPC connections on localhost.
/// Each connection gets access to the agent server's tools/list and tools/call
/// methods, plus any additional RPC targets registered before listening starts.
/// </summary>
public interface ITcpTransport : IDisposable
{
    /// <summary>
    /// Returns the number of currently active connections.
    /// </summary>
    int ActiveConnectionCount { get; }

    /// <summary>
    /// Fired when a new client connects. The parameter is the connection ID.
    /// </summary>
    event Action<int>? ConnectionAccepted;

    /// <summary>
    /// Fired when a client disconnects. The parameter is the connection ID.
    /// </summary>
    event Action<int>? ConnectionLost;

    /// <summary>
    /// Registers an additional RPC target object whose public methods will be
    /// exposed to all connections. Must be called before StartListeningAsync.
    /// </summary>
    void AddRpcTarget(object target);

    /// <summary>
    /// Starts listening for client connections on the specified TCP port.
    /// Accepts connections concurrently. Runs until the cancellation token
    /// is triggered or the transport is disposed.
    /// </summary>
    Task StartListeningAsync(int port, CancellationToken cancellationToken);
}
