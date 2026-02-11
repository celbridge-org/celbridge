using Celbridge.Python.Services;
using Microsoft.Extensions.Logging;

namespace Celbridge.Python;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IPythonConfigService, PythonConfigService>();
        services.AddTransient<IPythonService, PythonService>();
                
        services.AddTransient<Func<string, IRpcService>>(serviceProvider => pipeName =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RpcService>>();
            var handler = serviceProvider.GetRequiredService<PythonRpcHandler>();
            return new RpcService(logger, pipeName, handler);
        });

        services.AddTransient<Func<IRpcService, IPythonRpcClient>>(serviceProvider => rpcService =>
        {
            // Todo: Add any dependencies PythonRpcClient needs here
            return new PythonRpcClient(rpcService);
        });

        services.AddTransient<PythonRpcHandler>();

    }
}
