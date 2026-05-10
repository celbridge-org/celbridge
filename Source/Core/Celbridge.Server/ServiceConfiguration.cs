using Celbridge.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IFileServer, FileServer>();
        services.AddSingleton<IMcpToolBridge, McpToolBridge>();
        services.AddSingleton<AgentMonitor>();
        services.AddSingleton<IAgentServer, AgentServer>();
        services.AddSingleton<IServerService, ServerService>();
        services.AddSingleton<AgentReportBuilder>();
        services.AddSingleton<AgentReportBuilderRpcHandler>();
        services.AddTransient<ITcpTransport, TcpTransport>();
    }
}
