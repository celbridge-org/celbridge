using Celbridge.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server.Services;

/// <summary>
/// Manages the MCP agent server: registers the MCP endpoint on the shared
/// Kestrel instance so that MCP clients can connect to the server.
/// </summary>
internal class AgentServer : IAgentServer
{
    private readonly AgentMonitor _monitor;
    private readonly IGuides _guides;

    public AgentServer(AgentMonitor monitor, IGuides guides)
    {
        _monitor = monitor;
        _guides = guides;
    }

    /// <summary>
    /// Registers MCP SDK services and the tool assembly on the Kestrel
    /// service collection. Must be called during WebApplicationBuilder setup.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // Surface the application-scoped AgentMonitor singleton inside the
        // server scope as well, so the response filter and the diagnostics
        // RPC handler share one instance.
        services.AddSingleton(_monitor);

        var mcpBuilder = services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(AppTools).Assembly);

        var responseFilter = new AgentResponseFilter(_monitor, _guides);
        mcpBuilder.WithRequestFilters(filterBuilder => filterBuilder.AddCallToolFilter(responseFilter.CreateFilter()));
    }

    /// <summary>
    /// Maps the /mcp endpoint on the shared WebApplication.
    /// Must be called after building the WebApplication, before starting it.
    /// </summary>
    public void ConfigureEndpoints(WebApplication application)
    {
        application.MapMcp("/mcp");
    }
}
