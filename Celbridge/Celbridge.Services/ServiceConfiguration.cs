using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Services.Logging;
using Celbridge.Services.Messaging;
using Celbridge.Services.Settings;
using Celbridge.Services.UserInterface;

namespace Celbridge.Services;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<ISettingsGroup, SettingsGroup>();
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
    }
}