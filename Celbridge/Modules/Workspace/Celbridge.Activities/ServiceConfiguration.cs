using Celbridge.Activities.Services;
using Celbridge.Modules;

namespace Celbridge.Activities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //
        config.AddTransient<IActivityService, ActivityService>();
        config.AddTransient<ActivityRegistry>();
        config.AddTransient<ActivityDispatcher>();
    }
}
