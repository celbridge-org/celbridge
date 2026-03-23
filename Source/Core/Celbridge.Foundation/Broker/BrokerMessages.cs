namespace Celbridge.Broker;

/// <summary>
/// Sent after the MCP HTTP server has started and its port is known.
/// If a workspace is already loaded (startup race), the broker service
/// enables project file serving and writes the MCP config in response.
/// </summary>
public record McpServerReadyMessage();

/// <summary>
/// Sent when the project file server becomes ready to serve files.
/// Documents that depend on local file serving (e.g. .webapp) should
/// re-resolve and re-navigate their URLs when this message is received.
/// </summary>
public record ProjectFileServerReadyMessage();
