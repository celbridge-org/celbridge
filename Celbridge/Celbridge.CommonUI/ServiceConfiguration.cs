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
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<NewProjectPageViewModel>();
        services.AddTransient<WorkspacePageViewModel>();
    }

    public static void Initialize(Window mainWindow)
    {
        var userInterfaceService = Services.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        userInterfaceService.Initialize(mainWindow);

        userInterfaceService.RegisterPage(nameof(StartPage), typeof(StartPage));
        userInterfaceService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
        userInterfaceService.RegisterPage(nameof(NewProjectPage), typeof(NewProjectPage));
        userInterfaceService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));
    }
}
