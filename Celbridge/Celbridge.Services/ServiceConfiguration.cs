using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Services.Logging;
using Celbridge.Services.Messaging;
using Celbridge.Services.Navigation;
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
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();

        if (IsStorageAPIAvailable)
        {
            services.AddTransient<ISettingsGroup, SettingsGroup>();
        }
        else
        {
            services.AddTransient<ISettingsGroup, TempSettingsGroup>();
        }
    }

    private static bool IsStorageAPIAvailable
    {
        get
        {
#if WINDOWS
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                return package is not null;
            }
            catch (InvalidOperationException)
            {
                // Exception thrown if the app is unpackaged
                return false;
            }
#else
            return true;
#endif
        }

    }
}