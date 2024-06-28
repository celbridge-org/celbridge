using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.FilePicker;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Views;

namespace Celbridge.UserInterface;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<IDialogFactory, DialogFactory>();
        services.AddSingleton<INavigationService, NavigationService>();

        //
        // Register user interface services
        // These services can be acquired via the getters on IUserInterfaceService for convenient access.
        //
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();

        //
        // Register page view models
        //
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        //
        // Register dialog view models
        //
        services.AddTransient<AlertDialogViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<NewProjectDialogViewModel>();
        services.AddTransient<InputTextDialogViewModel>();
    }

    public static void Initialize()
    {
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(EmptyPage), typeof(EmptyPage));
        navigationService.RegisterPage(nameof(HomePage), typeof(HomePage));
        navigationService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
    }
}