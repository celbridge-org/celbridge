using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<StartViewModel>();
        services.AddTransient<TitleBar>();
        services.AddTransient<MainMenuViewModel>();
    }
}
