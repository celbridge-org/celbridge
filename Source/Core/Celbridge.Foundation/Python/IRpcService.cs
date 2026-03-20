namespace Celbridge.Python;

/// <summary>
/// A service for managing JSON-RPC communication with the Python connector over TCP.
/// Supports multiple sequential connections, allowing clients to disconnect and reconnect.
/// </summary>
public interface IRpcService : IDisposable
{
    /// <summary>
    /// Returns whether the RPC service has an active connection to a Python connector.
    /// </summary>
    bool IsConnected { get; }

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
    /// Accepts connections in a loop, allowing reconnection after disconnection.
    /// Runs until the cancellation token is triggered or the service is disposed.
    /// </summary>
    Task StartListeningAsync(int port, CancellationToken cancellationToken);
}
