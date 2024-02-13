using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.LiteDB;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Settings;
using Celbridge.CoreExtensions.Console;

namespace Celbridge.Dependencies;

public class ServiceConfiguration
{
    public static void Configure(IServiceCollection services)
    {
        // Services exposed via BaseLibrary interfaces
        services.AddTransient<ISettingsContainer, SettingsContainer>();
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConsoleService, ConsoleService>();

        // Internal services
        services.AddSingleton<LiteDBService>();
        services.AddTransient<LiteDBInstance>();
    }
}
