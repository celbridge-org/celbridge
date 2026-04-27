namespace Celbridge.Server;

/// <summary>
/// Sent when the file server becomes ready to serve files for a project.
/// Documents that depend on local file serving (e.g. .webapp) should
/// re-resolve and re-navigate their URLs when this message is received.
/// </summary>
public record ProjectFileServerReadyMessage();

/// <summary>
/// Sent when the server has been stopped, typically because the workspace
/// is being unloaded. Clients holding cached server state (e.g. an MCP
/// session id) should clear it so they reconnect against the next instance.
/// </summary>
public record ServerStoppedMessage();
