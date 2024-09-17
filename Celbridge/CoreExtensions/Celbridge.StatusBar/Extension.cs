using Celbridge.Extensions;
using Celbridge.Status.Services;
using Celbridge.Status.ViewModels;
using Celbridge.Status.Views;

namespace Celbridge.Status;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<IStatusService, StatusService>();
        config.AddTransient<IStatusPanel, StatusPanel>();
        config.AddTransient<StatusPanelViewModel>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
