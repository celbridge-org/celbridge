using Celbridge.Console;
using Celbridge.Python.Services;

namespace Celbridge.Python;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPythonConfigService, PythonConfigService>();
        services.AddSingleton<IPythonInstaller, PythonInstaller>();
        services.AddSingleton<IPythonLaunchService, PythonLaunchService>();
        services.AddTransient<IPythonService, PythonService>();

        services.AddSingleton<IConsoleSessionProvider, PythonSessionProvider>();
    }
}
