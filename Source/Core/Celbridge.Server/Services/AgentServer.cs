using Celbridge.Server.Helpers;
using Celbridge.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server.Services;

/// <summary>
/// Manages the MCP agent server: registers the MCP endpoint on the shared
/// Kestrel instance and writes/removes the .mcp.json configuration file
/// so that MCP clients can discover the server.
/// </summary>
public class AgentServer : IAgentServer
{
    private readonly ILogger<AgentServer> _logger;

    public AgentServer(ILogger<AgentServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers MCP SDK services and the tool assembly on the Kestrel
    /// service collection. Must be called during WebApplicationBuilder setup.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(AppTools).Assembly);
    }

    /// <summary>
    /// Maps the /mcp endpoint on the shared WebApplication.
    /// Must be called after building the WebApplication, before starting it.
    /// </summary>
    public void ConfigureEndpoints(WebApplication application)
    {
        application.MapMcp("/mcp");
    }

    public void Enable(string projectFolderPath, int port)
    {
        McpJsonConfigWriter.WriteConfigFile(projectFolderPath, port, _logger);
    }

    public void Disable(string projectFolderPath)
    {
        McpJsonConfigWriter.RemoveConfigEntry(projectFolderPath, _logger);
    }
}
