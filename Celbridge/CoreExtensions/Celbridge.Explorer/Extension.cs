using Celbridge.Commands.Services;
using Celbridge.Extensions;
using Celbridge.Explorer.Commands;
using Celbridge.Explorer.Services;
using Celbridge.Explorer.ViewModels;
using Celbridge.Explorer.Views;
using Celbridge.Validators;

namespace Celbridge.Explorer;

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
        config.AddTransient<IAddResourceCommand, AddResourceCommand>();
        config.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        config.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        config.AddTransient<IAddResourceDialogCommand, AddResourceDialogCommand>();
        config.AddTransient<IDeleteResourceDialogCommand, DeleteResourceDialogCommand>();
        config.AddTransient<IRenameResourceDialogCommand, RenameResourceDialogCommand>();
        config.AddTransient<IDuplicateResourceDialogCommand, DuplicateResourceDialogCommand>();
        config.AddTransient<ISelectResourceCommand, SelectResourceCommand>();
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
