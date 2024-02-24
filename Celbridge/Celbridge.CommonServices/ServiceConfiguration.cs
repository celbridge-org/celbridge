using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.Settings;
using Celbridge.CommonServices.UserInterface;
using Celbridge.CommonServices.ViewModels;

namespace Celbridge.CommonServices;

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

        //
        // Register view models
        //
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<NewProjectPageViewModel>();
        services.AddTransient<WorkspacePageViewModel>();
    }
}