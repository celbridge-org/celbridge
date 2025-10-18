using Celbridge.Commands.Services;
using Celbridge.Explorer.Commands;
using Celbridge.Explorer.Services;
using Celbridge.Explorer.ViewModels;
using Celbridge.Explorer.Views;
using Celbridge.Modules;
using Celbridge.Validators;

namespace Celbridge.Explorer;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IExplorerService, ExplorerService>();
        services.AddTransient<IResourceRegistry, ResourceRegistry>();
        services.AddTransient<IResourceRegistryDumper, ResourceRegistryDumper>();
        services.AddTransient<IResourceNameValidator, ResourceNameValidator>();
        services.AddTransient<ResourceArchiver>();
        services.AddTransient<ResourceChangeMonitor>();

        //
        // Register views
        //

        services.AddTransient<IExplorerPanel, ExplorerPanel>();
        services.AddTransient<ISearchPanel, SearchPanel>();

        //
        // Register view models
        //

        services.AddTransient<ExplorerPanelViewModel>();
        services.AddTransient<ResourceTreeViewModel>();

        //
        // Register commands
        //

        services.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        services.AddTransient<IAddResourceCommand, AddResourceCommand>();
        services.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        services.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        services.AddTransient<IAddResourceDialogCommand, AddResourceDialogCommand>();
        services.AddTransient<IDeleteResourceDialogCommand, DeleteResourceDialogCommand>();
        services.AddTransient<IRenameResourceDialogCommand, RenameResourceDialogCommand>();
        services.AddTransient<IDuplicateResourceDialogCommand, DuplicateResourceDialogCommand>();
        services.AddTransient<ISelectResourceCommand, SelectResourceCommand>();
        services.AddTransient<IExpandFolderCommand, ExpandFolderCommand>();
        services.AddTransient<IOpenFileManagerCommand, OpenFileManagerCommand>();
        services.AddTransient<IOpenApplicationCommand, OpenApplicationCommand>();
        services.AddTransient<IOpenBrowserCommand, OpenBrowserCommand>();
    }
}
