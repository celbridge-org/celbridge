using Celbridge.Python.Services;

namespace Celbridge.Python;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPythonConfigService, PythonConfigService>();
        services.AddTransient<IPythonService, PythonService>();
        services.AddTransient<PythonRpcHandler>();
    }
}
