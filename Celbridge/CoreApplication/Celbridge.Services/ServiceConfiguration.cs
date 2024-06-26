using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Settings;
using Celbridge.Services.Logging;
using Celbridge.Services.Messaging;
using Celbridge.Services.Project;
using Celbridge.Services.Settings;

namespace Celbridge.Services;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IEditorSettings, EditorSettings>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IMessengerService, MessengerService>();
        services.AddSingleton<IProjectDataService, ProjectDataService>();

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