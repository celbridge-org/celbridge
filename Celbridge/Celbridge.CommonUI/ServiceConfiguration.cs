using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<TitleBar>();
        services.AddTransient<WorkspacePageViewModel>();
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
    }

    public static void Initialize(Window mainWindow)
    {
        var userInterfaceService = Services.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        userInterfaceService.Initialize(mainWindow);

        userInterfaceService.RegisterPage(nameof(StartPage), typeof(StartPage));
        userInterfaceService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));
        userInterfaceService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
    }
}
