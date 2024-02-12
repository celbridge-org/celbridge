using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.ServiceLocator;
using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.LiteDB;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Settings;
using Celbridge.CoreExtensions.Console;

namespace Celbridge.Dependencies;

public class ServiceLocator : IServiceLocator
{
    private IServiceProvider? _serviceProvider;

    public static void ConfigureServices(IServiceCollection services)
    {
        // Register this service as the Service Locator
        services.AddSingleton<IServiceLocator, ServiceLocator>();

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

    public void Initialize(IServiceProvider serviceProvider)
    {
        if (serviceProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        _serviceProvider = serviceProvider;
    }

    public T GetRequiredService<T>() where T : notnull
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException();
        }
        return _serviceProvider.GetRequiredService<T>();
    }
}
