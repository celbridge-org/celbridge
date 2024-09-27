using Celbridge.Extensions;
using Celbridge.Status.Services;
using Celbridge.Status.ViewModels;
using Celbridge.Status.Views;

namespace Celbridge.Status;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register views
        //
        config.AddTransient<IStatusPanel, StatusPanel>();

        //
        // Register view models
        //
        config.AddTransient<StatusPanelViewModel>();

        //
        // Register services
        //
        config.AddTransient<IStatusService, StatusService>();
    }
}
