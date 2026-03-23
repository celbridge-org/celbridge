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
/// Coordinates the server infrastructure. Starts the HTTP server during
/// application initialization and manages the lifecycle of the agent server,
/// file server, and other server components in response to workspace events.
/// </summary>
public interface IServerService
{
    /// <summary>
    /// The current state of the server infrastructure.
    /// Project loading should not proceed until this is Ready.
    /// </summary>
    ServerStatus Status { get; }

    /// <summary>
    /// The port the HTTP server is listening on, or 0 if not started.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Starts the HTTP server and registers all endpoints.
    /// Sets Status to Ready on completion.
    /// </summary>
    Task InitializeAsync();
}
