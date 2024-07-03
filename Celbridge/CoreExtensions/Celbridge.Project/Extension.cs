using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Project;
using Celbridge.Project.Commands;
using Celbridge.Project.Services;
using Celbridge.Project.ViewModels;
using Celbridge.Project.Views;

namespace Celbridge.Project;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register UI elements
        //
        config.AddTransient<ProjectPanel>();

        //
        // Register View Models
        //
        config.AddTransient<ProjectPanelViewModel>();
        config.AddTransient<ResourceTreeViewModel>();

        //
        // Register services
        //
        config.AddTransient<IProjectService, ProjectService>();

        //
        // Register commands
        //
        config.AddTransient<IRefreshResourceTreeCommand, RefreshResourceTreeCommand>();
        config.AddTransient<IAddFolderCommand, AddFolderCommand>();
        config.AddTransient<IAddFileCommand, AddFileCommand>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
