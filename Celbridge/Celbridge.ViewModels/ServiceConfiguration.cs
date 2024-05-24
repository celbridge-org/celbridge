using Celbridge.ViewModels.Dialogs;
using Celbridge.ViewModels.Pages;

namespace Celbridge.ViewModels;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register page view models
        //
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<NewProjectPageViewModel>();

        //
        // Register dialog view models
        //
        services.AddTransient<AlertDialogViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<NewProjectDialogViewModel>();
    }
}