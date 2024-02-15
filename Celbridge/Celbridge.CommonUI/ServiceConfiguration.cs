using Celbridge.CommonUI.UserInterface;
using Celbridge.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.CommonUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<WorkspaceViewModel>();
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
    }
}
