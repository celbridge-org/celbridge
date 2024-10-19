using Celbridge.Modules;
using Celbridge.ResourceData.Services;

namespace Celbridge.ResourceData;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<IResourceDataService, ResourceDataService>();
    }
}
