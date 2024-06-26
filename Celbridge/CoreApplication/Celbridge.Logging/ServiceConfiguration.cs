using Celbridge.BaseLibrary.Logging;
using Celbridge.Logging.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Logging;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<ILoggingService, LoggingService>();
    }
}