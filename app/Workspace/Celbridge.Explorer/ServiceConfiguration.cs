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
        services.AddTransient<ISearchService, SearchService>();

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
        services.AddTransient<SearchPanelViewModel>();

        //
        // Register commands
        //

        services.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        services.AddTransient<IAddResourceCommand, AddResourceCommand>();
        services.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        services.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        services.AddTransient<ITransferResourcesCommand, TransferResourcesCommand>();
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
