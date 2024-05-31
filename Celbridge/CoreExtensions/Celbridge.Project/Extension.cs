using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Project.ViewModels;
using Celbridge.Project.Views;

namespace Celbridge.Project;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<ProjectPanel>();
        config.AddTransient<ProjectPanelViewModel>();
        config.AddTransient<IProjectService, ProjectService>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
