using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.Views.Dialogs;
using Celbridge.Views.Pages;

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

        navigationService.RegisterPage(nameof(EmptyPage), typeof(EmptyPage));
        navigationService.RegisterPage(nameof(HomePage), typeof(HomePage));
        navigationService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
    }
}
