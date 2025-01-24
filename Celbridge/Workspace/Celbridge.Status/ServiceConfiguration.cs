using Celbridge.Modules;
using Celbridge.Status.Services;
using Celbridge.Status.ViewModels;
using Celbridge.Status.Views;

namespace Celbridge.Status;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register views
        //
        services.AddTransient<IStatusPanel, StatusPanel>();

        //
        // Register view models
        //
        services.AddTransient<StatusPanelViewModel>();

        //
        // Register services
        //
        services.AddTransient<IStatusService, StatusService>();
    }
}
