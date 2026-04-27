namespace Celbridge.Server;

/// <summary>
/// Describes the current state of the server infrastructure.
/// </summary>
public enum ServerStatus
{
    NotStarted,
    Starting,
    Ready,
    Error
}

/// <summary>
/// Coordinates the server infrastructure. Starts a fresh HTTP server when a
/// workspace is loaded and stops it when the workspace is unloaded. The same
/// port is reused for the lifetime of the application so URLs remain stable
/// across project switches.
/// </summary>
public interface IServerService
{
    /// <summary>
    /// The current state of the server infrastructure.
    /// </summary>
    ServerStatus Status { get; }

    /// <summary>
    /// The port the HTTP server is listening on, or 0 if not started.
    /// Once assigned, the port is reused on subsequent starts within the
    /// same application session.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Builds and starts a fresh HTTP server instance for the loaded workspace.
    /// Reuses the port assigned on the first call within this application session.
    /// Sets Status to Ready on completion.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops and disposes the HTTP server instance. The assigned port is retained
    /// so the next StartAsync call binds to the same port.
    /// </summary>
    Task StopAsync();
}
