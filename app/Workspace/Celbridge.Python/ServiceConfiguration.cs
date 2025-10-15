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
        services.AddTransient<IPythonService, PythonService>();
        
        services.AddTransient<IPythonRpcClient, PythonRpcClient>();
        services.AddTransient<PythonRpcHandler>();
        
        services.AddTransient<Func<string, IRpcService>>(serviceProvider => pipeName =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RpcService>>();
            var handler = serviceProvider.GetRequiredService<PythonRpcHandler>();
            return new RpcService(logger, pipeName, handler);
        });
    }
}
