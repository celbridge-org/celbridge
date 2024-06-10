using Celbridge.BaseLibrary.UserInterface.Dialog;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.Views.Dialogs;
using Celbridge.Views.Pages;
using Celbridge.Views.UserControls;

namespace Celbridge.Views;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDialogFactory, DialogFactory>();
    }

    public static void Initialize()
    {
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(StartPage), typeof(StartPage));
        navigationService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
    }
}
