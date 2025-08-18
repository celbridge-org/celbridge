using Celbridge.Python.Services;

namespace Celbridge.Python;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddTransient<IPythonService, PythonService>();
    }
}
