using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Inspector;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;

namespace Celbridge.Inspector;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddSingleton<IInspectorService, InspectorService>();

        config.AddTransient<InspectorPanel>();
        config.AddTransient<InspectorPanelViewModel>();
    }

    public Result Initialize()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();

        userInterfaceService.RegisterWorkspacePanelConfig(
            new WorkspacePanelConfig(WorkspacePanelType.InspectorPanel, typeof(InspectorPanel)));

        return Result.Ok();
    }
}
