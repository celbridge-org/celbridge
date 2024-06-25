using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Status.Services;
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
        return Result.Ok();
    }
}
