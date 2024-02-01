using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.CommonServices.Logging;
using Celbridge.CoreExtensions.Console;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.DIContainer;

public static class CelServiceRegistration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConsoleService, ConsoleService>();

        // Other service registrations...
    }
}
