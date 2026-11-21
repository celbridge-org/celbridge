using Celbridge.Console;
using Celbridge.Dependencies;
using Celbridge.Python.Services;

namespace Celbridge.Python;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPythonConfigService, PythonConfigService>();
        services.AddSingleton<CelbridgeWheelFolders>();
        services.AddSingleton<IUvRunner, UvRunner>();
        services.AddSingleton<ITemporaryEnvironments, TemporaryEnvironments>();
        services.AddSingleton<IPythonLaunchService, PythonLaunchService>();

        services.AddSingleton<IConsoleEnvironmentContributor, PythonEnvironmentContributor>();

        services.AddSingleton<UvDependency>();
        services.AddSingleton<CelbridgeWheelDependency>();
        services.AddSingleton<BasedpyrightDependency>();
        services.AddSingleton<IPythonLanguageServerEnvironment, PythonLanguageServerEnvironment>();
        services.AddSingleton<IFeatureDependencies, PythonFeatureDependencies>();
    }

    public static void Initialize()
    {
        // No console runs yet at application start, so every temporary environment in uv's cache comes from an
        // earlier run. The removal runs in the background.
        var temporaryEnvironments = ServiceLocator.AcquireService<ITemporaryEnvironments>();
        _ = Task.Run(temporaryEnvironments.RemoveOrphanedAsync);
    }
}
