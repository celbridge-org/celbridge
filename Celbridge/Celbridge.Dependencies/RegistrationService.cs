using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.CommonServices.Logging;
using Celbridge.CoreExtensions.Console;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.Dependencies;

public static class RegistrationService
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConsoleService, ConsoleService>();

        // Other service registrations...
    }
}
