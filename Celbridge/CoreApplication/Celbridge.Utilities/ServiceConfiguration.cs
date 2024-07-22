using Celbridge.Messaging.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Utilities;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IUtilityService, UtilityService>();
    }
}