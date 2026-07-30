using Celbridge.Console;
using Celbridge.Platform;
using Celbridge.Python.Services;

namespace Celbridge.Python;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPythonConfigService, PythonConfigService>();
        services.AddSingleton<IPythonInstaller, PythonInstaller>();
        services.AddSingleton<IPythonLaunchService, PythonLaunchService>();

        services.AddSingleton<IConsoleSessionProvider, PythonSessionProvider>();
        services.AddSingleton<IConsoleEnvironmentContributor, PythonEnvironmentContributor>();
    }

    public static void Initialize()
    {
        // Start installing the app-level Python support files (the uv binary and the celbridge wheel) as the
        // app loads, so a new install or an upgrade extracts before any workspace opens rather than when the
        // first console launches. The install is single-flight, so a console that starts before it finishes
        // awaits this same run.
        var appEnvironment = ServiceLocator.AcquireService<IAppEnvironment>();
        var pythonInstaller = ServiceLocator.AcquireService<IPythonInstaller>();

        var environmentInfo = appEnvironment.GetEnvironmentInfo();
        _ = pythonInstaller.InstallPythonAsync(environmentInfo.AppVersion);
    }
}
