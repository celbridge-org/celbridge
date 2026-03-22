using Celbridge.Broker.Services;
using Celbridge.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Broker;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<ToolExecutor>();
        services.AddSingleton<IBrokerService, BrokerService>();
        services.AddSingleton<BrokerRpcHandler>();
        services.AddSingleton<IMcpHttpTransport, McpHttpTransport>();
        services.AddTransient<ITcpTransport, TcpTransport>();
    }

    public static void Initialize()
    {
        var brokerService = ServiceLocator.AcquireService<IBrokerService>();
        brokerService.Initialize(AppDomain.CurrentDomain.GetAssemblies());

        _ = StartMcpServerAsync();
    }

    private static async Task StartMcpServerAsync()
    {
        try
        {
            var mcpTransport = ServiceLocator.AcquireService<IMcpHttpTransport>();
            await mcpTransport.StartAsync();
        }
        catch (Exception exception)
        {
            var logger = ServiceLocator.AcquireService<ILogger<McpHttpTransport>>();
            logger.LogError(exception, "Failed to start MCP HTTP server");
        }
    }
}
