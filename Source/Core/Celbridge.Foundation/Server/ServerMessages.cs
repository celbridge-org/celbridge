namespace Celbridge.Server;

/// <summary>
/// Sent when the file server becomes ready to serve files for a project.
/// Documents that depend on local file serving (e.g. .webapp) should
/// re-resolve and re-navigate their URLs when this message is received.
/// </summary>
public record ProjectFileServerReadyMessage();
