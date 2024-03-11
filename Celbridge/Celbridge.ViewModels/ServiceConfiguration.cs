using Celbridge.ViewModels.Pages;

namespace Celbridge.ViewModels;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register view models
        //
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<NewProjectPageViewModel>();
    }
}