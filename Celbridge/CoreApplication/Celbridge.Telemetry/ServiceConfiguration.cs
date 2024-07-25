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

        // TelemetryLogger uses DI to acquire dependencies, but it's not exposed publicly so doesn't require an interface
        services.AddTransient<TelemetryLogger>();
    }
}