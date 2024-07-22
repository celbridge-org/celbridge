using Celbridge.Extensions;
using Celbridge.Inspector.Services;
using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;

namespace Celbridge.Inspector;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<InspectorPanel>();
        config.AddTransient<InspectorPanelViewModel>();
        config.AddTransient<IInspectorService, InspectorService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
