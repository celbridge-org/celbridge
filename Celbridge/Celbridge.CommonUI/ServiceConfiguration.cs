using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<WorkspacePageViewModel>();
        services.AddTransient<StartPageViewModel>();
        services.AddTransient<TitleBar>();
        services.AddTransient<MainMenuViewModel>();
    }
}
