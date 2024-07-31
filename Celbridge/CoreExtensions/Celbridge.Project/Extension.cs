using Celbridge.Extensions;
using Celbridge.Validators;
using Celbridge.Projects.Commands;
using Celbridge.Projects.Services;
using Celbridge.Projects.ViewModels;
using Celbridge.Projects.Views;
using Celbridge.Resources;
using Celbridge.Commands.Services;

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
        config.AddTransient<IProjectService, ProjectService>();
        config.AddTransient<IResourceRegistry, ResourceRegistry>();
        config.AddTransient<IResourceRegistryDumper, ResourceRegistryDumper>();

        //
        // Register commands
        //
        config.AddTransient<IUpdateResourceRegistryCommand, UpdateResourceRegistryCommand>();
        config.AddTransient<IAddResourceCommand, AddResourceCommand>();
        config.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        config.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        config.AddTransient<ICopyResourceToClipboardCommand, CopyResourceToClipboardCommand>();
        config.AddTransient<IPasteResourceFromClipboardCommand, PasteResourceFromClipboardCommand>();
        config.AddTransient<IAddResourceDialogCommand, AddResourceDialogCommand>();
        config.AddTransient<IDeleteResourceDialogCommand, DeleteResourceDialogCommand>();
        config.AddTransient<IRenameResourceDialogCommand, RenameResourceDialogCommand>();
        config.AddTransient<IDuplicateResourceDialogCommand, DuplicateResourceDialogCommand>();

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
