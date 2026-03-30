using Celbridge.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IFileServer, FileServer>();
        services.AddSingleton<McpToolBridge>();
        services.AddSingleton<IAgentServer, Services.AgentServer>();
        services.AddSingleton<IServerService, ServerService>();
        services.AddTransient<ITcpTransport, TcpTransport>();
    }

    public static void Initialize()
    {
        var serverService = ServiceLocator.AcquireService<IServerService>();
        _ = serverService.InitializeAsync();
    }
}
