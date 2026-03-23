namespace Celbridge.Server;

/// <summary>
/// Manages the MCP agent server: configuration file management and
/// MCP endpoint setup.
/// </summary>
public interface IAgentServer
{
    /// <summary>
    /// Enables the agent server for the given project, writing the MCP
    /// config file so that MCP clients can discover the server.
    /// </summary>
    void Enable(string projectFolderPath, int port);

    /// <summary>
    /// Disables the agent server for the given project, removing the MCP
    /// config file entry.
    /// </summary>
    void Disable(string projectFolderPath);
}
