namespace Celbridge.Broker;

/// <summary>
/// An HTTP transport that hosts an MCP server on localhost using Kestrel.
/// AI agents (e.g. Claude CLI) connect to this server to discover and
/// invoke broker tools via the standard MCP protocol.
/// </summary>
public interface IMcpHttpTransport : IDisposable
{
    /// <summary>
    /// The port the MCP server is listening on, or 0 if not started.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Starts the MCP HTTP server on a dynamically assigned port.
    /// Returns once the server is ready to accept connections.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the MCP HTTP server.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
