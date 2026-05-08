using Celbridge.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server.Services;

/// <summary>
/// Manages the MCP agent server: registers the MCP endpoint on the shared
/// Kestrel instance so that MCP clients can connect to the server.
/// </summary>
public class AgentServer : IAgentServer
{
    private readonly ToolTelemetry _telemetry;

    public AgentServer(ToolTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    /// <summary>
    /// Registers MCP SDK services and the tool assembly on the Kestrel
    /// service collection. Must be called during WebApplicationBuilder setup.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // Surface the application-scoped ToolTelemetry singleton inside the
        // server scope as well, so the cold-start gate filter and the
        // diagnostics RPC handler share one instance.
        services.AddSingleton(_telemetry);

        var mcpBuilder = services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(AppTools).Assembly);

        var toolGateFilter = ToolGate.CreateFilter(_telemetry);
        mcpBuilder.WithRequestFilters(filterBuilder => filterBuilder.AddCallToolFilter(toolGateFilter));
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
