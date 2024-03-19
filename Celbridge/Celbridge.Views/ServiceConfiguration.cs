using Celbridge.BaseLibrary.Navigation;
using Celbridge.Views.Pages;
using Celbridge.Views.UserControls;

namespace Celbridge.Views;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<TitleBar>();
    }

    public static void Initialize()
    {
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(StartPage), typeof(StartPage));
        navigationService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
        navigationService.RegisterPage(nameof(NewProjectPage), typeof(NewProjectPage));
    }
}
