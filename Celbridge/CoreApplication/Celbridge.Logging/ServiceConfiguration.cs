using Celbridge.Logging.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace Celbridge.Logging;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddNLog("NLog.config");
        });

        //
        // Register services
        //

        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Services.Logger<>)));
        services.AddTransient<ILogSerializer, LogSerializer>();
    }
}