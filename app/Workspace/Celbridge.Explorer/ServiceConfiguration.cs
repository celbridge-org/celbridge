using Celbridge.Explorer.Commands;
using Celbridge.Explorer.Services;
using Celbridge.Explorer.ViewModels;
using Celbridge.Explorer.Views;

namespace Celbridge.Explorer;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddTransient<IExplorerService, ExplorerService>();
        services.AddTransient<IFolderStateService, FolderStateService>();

        //
        // Register views
        //

        services.AddTransient<IExplorerPanel, ExplorerPanel>();

        //
        // Register view models
        //

        services.AddTransient<ExplorerPanelViewModel>();
        services.AddTransient<ResourceViewViewModel>();

        //
        // Register commands
        //

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
