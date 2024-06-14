using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.FilePicker;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Services.Dialog;
using Celbridge.Services.FilePicker;
using Celbridge.Services.Logging;
using Celbridge.Services.Messaging;
using Celbridge.Services.Navigation;
using Celbridge.Services.Project;
using Celbridge.Services.Settings;
using Celbridge.Services.UserInterface;
using Celbridge.Services.Workspace;

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
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IProjectDataService, ProjectDataService>();

        // Register user interface services
        // These services can be acquired via the getters on IUserInterfaceService for convenient access.
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();

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