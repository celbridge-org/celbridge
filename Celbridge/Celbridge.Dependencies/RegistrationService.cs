using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CoreExtensions.Console;

namespace Celbridge.Dependencies;

public static class RegistrationService
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConsoleService, ConsoleService>();
    }
}
