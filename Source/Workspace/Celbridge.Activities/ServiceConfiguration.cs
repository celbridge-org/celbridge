using Celbridge.Activities.Services;

namespace Celbridge.Activities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddTransient<IActivityService, ActivityService>();
        services.AddTransient<ActivityRegistry>();
        services.AddTransient<ActivityDispatcher>();
    }
}
