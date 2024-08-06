using Celbridge.Commands.Services;
using Celbridge.Extensions;
using Celbridge.Project.Services;
using Celbridge.Projects.Commands;
using Celbridge.Projects.Services;
using Celbridge.Projects.ViewModels;
using Celbridge.Projects.Views;
using Celbridge.Resources;
using Celbridge.Validators;

namespace Celbridge.Projects;

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
        config.AddTransient<IResourceService, ResourceService>();
        config.AddTransient<IResourceRegistry, ResourceRegistry>();
        config.AddTransient<IResourceRegistryDumper, ResourceRegistryDumper>();
        config.AddTransient<IResourceTransfer, ResourceTransfer>();

        //
        // Register commands
        //
        config.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        config.AddTransient<IAddResourceCommand, AddResourceCommand>();
        config.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        config.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        config.AddTransient<ICopyResourceToClipboardCommand, CopyResourceToClipboardCommand>();
        config.AddTransient<IPasteResourceFromClipboardCommand, PasteResourceFromClipboardCommand>();
        config.AddTransient<IAddResourceDialogCommand, AddResourceDialogCommand>();
        config.AddTransient<IDeleteResourceDialogCommand, DeleteResourceDialogCommand>();
        config.AddTransient<IRenameResourceDialogCommand, RenameResourceDialogCommand>();
        config.AddTransient<IDuplicateResourceDialogCommand, DuplicateResourceDialogCommand>();
        config.AddTransient<IExpandFolderCommand, ExpandFolderCommand>();

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
