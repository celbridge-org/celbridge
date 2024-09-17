using Celbridge.Extensions;
using Celbridge.Inspector.Services;
using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;

namespace Celbridge.Inspector;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register UI elements
        //
        config.AddTransient<InspectorPanel>();

        //
        // Register View Models
        //
        config.AddTransient<InspectorPanelViewModel>();

        //
        // Register services
        //
        config.AddTransient<IInspectorService, InspectorService>();
    }

    public static Result Initialize()
    {
        return Result.Ok();
    }
}
