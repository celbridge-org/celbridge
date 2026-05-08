using Celbridge.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IFileServer, FileServer>();
        services.AddSingleton<IMcpToolBridge, McpToolBridge>();
        services.AddSingleton<ToolTelemetry>();
        services.AddSingleton<IAgentServer, AgentServer>();
        services.AddSingleton<IServerService, ServerService>();
        services.AddSingleton<AgentAnalytics>();
        services.AddSingleton<AgentAnalyticsRpcHandler>();
        services.AddTransient<ITcpTransport, TcpTransport>();
    }
}
