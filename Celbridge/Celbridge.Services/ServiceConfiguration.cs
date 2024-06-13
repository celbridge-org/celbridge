using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface.Dialog;
using Celbridge.BaseLibrary.UserInterface.FilePicker;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Services.Logging;
using Celbridge.Services.Messaging;
using Celbridge.Services.Settings;
using Celbridge.Services.UserInterface.Dialog;
using Celbridge.Services.UserInterface.FilePicker;
using Celbridge.Services.UserInterface.Navigation;
using Celbridge.Services.UserInterface;
using Celbridge.BaseLibrary.Project;
using Celbridge.Services.Project;
using Celbridge.BaseLibrary.Commands;
using Celbridge.Services.Commands;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Services.Workspace;

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
        services.AddSingleton<ICommandService, CommandService>();
        services.AddSingleton<IProjectDataService, ProjectDataService>();

        // Register user interface services
        // These services can be acquired via the getters on IUserInterfaceService for convenient access.
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();

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