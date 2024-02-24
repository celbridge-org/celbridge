using Celbridge.CommonServices.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<TitleBar>();
    }

    public static void Initialize()
    {
        var navigationService = Services.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(StartPage), typeof(StartPage));
        navigationService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
        navigationService.RegisterPage(nameof(NewProjectPage), typeof(NewProjectPage));
        navigationService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));
    }
}
