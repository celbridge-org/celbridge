using Celbridge.Modules;
using Celbridge.Inspector.Services;
using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;

namespace Celbridge.Inspector;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IInspectorService, InspectorService>();
        services.AddTransient<IInspectorFactory, InspectorFactory>();
        services.AddTransient<ComponentEditorCache>();
        services.AddTransient<EntityAnnotationCache>();

        //
        // Register views
        //

        services.AddTransient<IInspectorPanel, InspectorPanel>();

        //
        // Register view models
        //

        services.AddTransient<InspectorPanelViewModel>();
        services.AddTransient<ResourceNameInspectorViewModel>();
        services.AddTransient<WebInspectorViewModel>();
        services.AddTransient<MarkdownInspectorViewModel>();
        services.AddTransient<ComponentListViewModel>();
        services.AddTransient<EntityEditorViewModel>();
        services.AddTransient<ComponentValueEditorViewModel>();
        services.AddTransient<ComponentTypeEditorViewModel>();
    }

    public static Result Initialize()
    {
        return Result.Ok();
    }
}
