using Celbridge.Resources.Services;

namespace Celbridge.Resources;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IResourceService, ResourceService>();
    }
}
