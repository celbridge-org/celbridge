using Celbridge.CoreExtensions.Console;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.LiteDB;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Console;

namespace Celbridge.Dependencies;

public static class RegistrationService
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // Services exposed via BaseLibrary interfaces
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConsoleService, ConsoleService>();

        // Internal services
        services.AddSingleton<LiteDBService>();
        services.AddTransient<LiteDBInstance>();
    }
}
