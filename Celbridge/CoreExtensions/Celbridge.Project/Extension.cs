using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Project.ViewModels;
using Celbridge.Project.Views;

namespace Celbridge.Project;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddSingleton<IProjectService, ProjectService>();

        config.AddTransient<ProjectPanel>();
        config.AddTransient<ProjectPanelViewModel>();
    }

    public Result Initialize()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();

        userInterfaceService.RegisterWorkspacePanelConfig(
            new WorkspacePanelConfig(WorkspacePanelType.ProjectPanel, typeof(ProjectPanel)));

        return Result.Ok();
    }
}
