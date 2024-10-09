using Celbridge.Modules;
using Celbridge.Inspector.Services;
using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;
using Celbridge.Foundation;

namespace Celbridge.Inspector;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IModuleServiceCollection config)
    {
        //
        // Register services
        //

        config.AddTransient<IInspectorService, InspectorService>();

        //
        // Register views
        //

        config.AddTransient<IInspectorPanel, InspectorPanel>();


        //
        // Register view models
        //

        config.AddTransient<InspectorPanelViewModel>();

    }

    public static Result Initialize()
    {
        return Result.Ok();
    }
}
