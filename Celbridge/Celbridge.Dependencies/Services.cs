using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.LiteDB;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Settings;

namespace Celbridge.Dependencies;

public class Services
{
    public static void Configure(IServiceCollection services)
    {
        ConfigureCommonServices(services);
        ConfigureCoreExtensions(services);

        // Internal services
        services.AddSingleton<LiteDBService>();
        services.AddTransient<LiteDBInstance>();
    }

    private static void ConfigureCommonServices(IServiceCollection services)
    {
        services.AddTransient<ISettingsContainer, SettingsContainer>();
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
    }

    private static void ConfigureCoreExtensions(IServiceCollection services)
    {
        var configuration = new ServiceConfiguration();

        // Todo: Use reflection to discover all extension classes automatically

        var shellExtension = new Shell.ShellExtension();
        shellExtension.ConfigureServices(configuration);

        var consoleExtension = new Console.ConsoleExtension();
        consoleExtension.ConfigureServices(configuration);

        configuration.ConfigureServices(services);
    }
}
