using Celbridge.Commands.Services;
using Celbridge.Extensions;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Resources.ViewModels;
using Celbridge.Resources.Views;
using Celbridge.Validators;

namespace Celbridge.Resources;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register UI elements
        //
        config.AddTransient<ResourcesPanel>();

        //
        // Register View Models
        //
        config.AddTransient<ResourcesPanelViewModel>();
        config.AddTransient<ResourceTreeViewModel>();

        //
        // Register services
        //
        config.AddTransient<IResourceService, ResourceService>();
        config.AddTransient<IResourceRegistry, ResourceRegistry>();
        config.AddTransient<IResourceRegistryDumper, ResourceRegistryDumper>();

        //
        // Register commands
        //
        config.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        config.AddTransient<IOpenFileResourceCommand, OpenFileResourceCommand>();
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
