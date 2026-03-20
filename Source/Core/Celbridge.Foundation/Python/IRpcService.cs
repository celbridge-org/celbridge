namespace Celbridge.Python;

/// <summary>
/// A service for managing JSON-RPC communication with the Python connector over TCP.
/// Supports multiple simultaneous connections, allowing several Python clients to
/// interact with the host application concurrently.
/// </summary>
public interface IRpcService : IDisposable
{
    /// <summary>
    /// Returns the number of currently active Python connector connections.
    /// </summary>
    int ActiveConnectionCount { get; }

    /// <summary>
    /// Fired when a new Python connector connects. The parameter is the connection ID.
    /// </summary>
    event Action<int>? ConnectionAccepted;

    /// <summary>
    /// Fired when a Python connector disconnects. The parameter is the connection ID.
    /// </summary>
    event Action<int>? ConnectionLost;

    /// <summary>
    /// Starts listening for Python connector connections on the specified TCP port.
    /// Accepts connections concurrently, allowing multiple clients at the same time.
    /// Runs until the cancellation token is triggered or the service is disposed.
    /// </summary>
    Task StartListeningAsync(int port, CancellationToken cancellationToken);
}
