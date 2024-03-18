using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.StatusBar.ViewModels;
using Celbridge.StatusBar.Views;

namespace Celbridge.StatusBar;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<StatusPanel>();
        config.AddTransient<StatusPanelViewModel>();
        config.AddTransient<IStatusService, StatusService>();
    }

    public Result Initialize()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();

        userInterfaceService.RegisterWorkspacePanelConfig(
            new WorkspacePanelConfig(WorkspacePanelType.StatusPanel, typeof(StatusPanel)));

        return Result.Ok();
    }
}
