using Celbridge.Broker.Services;
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
        services.AddTransient<ITcpTransport, TcpTransport>();
    }

    public static void Initialize()
    {
        var brokerService = ServiceLocator.AcquireService<IBrokerService>();
        brokerService.Initialize(AppDomain.CurrentDomain.GetAssemblies());
    }
}
