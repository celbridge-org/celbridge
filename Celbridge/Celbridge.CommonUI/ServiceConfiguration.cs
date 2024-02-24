using Celbridge.CommonServices.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<TitleBar>();
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<NewProjectPageViewModel>();
        services.AddTransient<WorkspacePageViewModel>();
    }

    public static void Initialize()
    {
        var userInterfaceService = Services.ServiceProvider.GetRequiredService<IUserInterfaceService>();

        userInterfaceService.RegisterPage(nameof(StartPage), typeof(StartPage));
        userInterfaceService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
        userInterfaceService.RegisterPage(nameof(NewProjectPage), typeof(NewProjectPage));
        userInterfaceService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));
    }
}
