using Celbridge.Telemetry.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Telemetry;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddTransient<TelemetryLogger>();
    }
}