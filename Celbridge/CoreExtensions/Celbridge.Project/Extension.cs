using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Validators;
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
        config.AddTransient<IUpdateResourceTreeCommand, UpdateResourceTreeCommand>();
        config.AddTransient<IAddResourceCommand, AddResourceCommand>();
        config.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        config.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        config.AddTransient<ICopyResourceToClipboardCommand, CopyResourceToClipboardCommand>();
        config.AddTransient<IPasteResourceFromClipboardCommand, PasteResourceFromClipboardCommand>();
        config.AddTransient<IShowAddResourceDialogCommand, ShowAddResourceDialogCommand>();
        config.AddTransient<IShowDeleteResourceDialogCommand, ShowDeleteResourceDialogCommand>();
        config.AddTransient<IShowRenameResourceDialogCommand, ShowRenameResourceDialogCommand>();
        config.AddTransient<IShowDuplicateResourceDialogCommand, ShowDuplicateResourceDialogCommand>();

        //
        // Register validators
        //
        config.AddTransient<IResourceNameValidator, ResourceNameValidator>();
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
