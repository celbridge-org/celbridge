using Celbridge.Dependencies.Services;

namespace Celbridge.Dependencies;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<IDependencyService, DependencyService>();
        services.AddSingleton<IDependencyDownloader, DependencyDownloader>();
    }
}
