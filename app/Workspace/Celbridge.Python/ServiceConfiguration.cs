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
        
        // Register RPC service factory (creates RpcService instances with specific pipe names)
        services.AddTransient<Func<string, IRpcService>>(serviceProvider => pipeName =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RpcService>>();
            return new RpcService(logger, pipeName);
        });
    }
}
