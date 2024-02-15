using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<StartViewModel>();
    }
}
