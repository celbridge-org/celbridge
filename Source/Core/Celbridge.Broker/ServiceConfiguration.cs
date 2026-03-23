using Celbridge.Broker.Services;
using Celbridge.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Broker;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The MCP HTTP server runs in its own Kestrel instance with a separate
        // DI container. IApplicationServiceProvider wraps the main app's service
        // provider so that MCP tool classes can resolve application services.
        services.AddSingleton<IApplicationServiceProvider>(provider =>
        {
            return new ApplicationServiceProvider(provider);
        });

        services.AddSingleton<ProjectFileServer>();
        services.AddSingleton<IProjectFileServer>(provider => provider.GetRequiredService<ProjectFileServer>());
        services.AddSingleton<BrokerRpcHandler>();
        services.AddSingleton<IMcpHttpTransport, McpHttpTransport>();
        services.AddSingleton<IBrokerService, BrokerService>();
        services.AddTransient<ITcpTransport, TcpTransport>();
    }

    public static void Initialize()
    {
        _ = StartMcpServerAsync();
    }

    private static async Task StartMcpServerAsync()
    {
        try
        {
            var mcpTransport = ServiceLocator.AcquireService<IMcpHttpTransport>();
            await mcpTransport.StartAsync();

            // Ensure the broker service is instantiated so its message
            // handler is registered before we send the ready message.
            ServiceLocator.AcquireService<IBrokerService>();

            var messengerService = ServiceLocator.AcquireService<IMessengerService>();
            messengerService.Send(new McpServerReadyMessage());
        }
        catch (Exception exception)
        {
            var logger = ServiceLocator.AcquireService<ILogger<McpHttpTransport>>();
            logger.LogError(exception, "Failed to start MCP HTTP server");
        }
    }
}
