using Celbridge.Messaging.Services;
using Celbridge.Utilities.Services;

namespace Celbridge.Utilities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IUtilityService, UtilityService>();

        services.AddTransient<ILogSerializer, LogSerializer>();
        services.AddTransient<ILogger, Logger>();
    }
}