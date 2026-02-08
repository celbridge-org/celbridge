using Celbridge.ContextMenu;
using Celbridge.Explorer.Commands;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Explorer.Services;
using Celbridge.Explorer.ViewModels;
using Celbridge.Explorer.Views;
using Celbridge.UserInterface.ContextMenu;

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
        services.AddTransient<ResourceTreeViewModel>();

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
        services.AddTransient<ICopyResourceKeyCommand, CopyResourceKeyCommand>();
        services.AddTransient<ICopyFilePathCommand, CopyFilePathCommand>();

        //
        // Register menu system
        //

        services.AddSingleton<IMenuBuilder<ExplorerMenuContext>, ExplorerMenuBuilder>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, RunMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, OpenMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, AddFileMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, AddFolderMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, CutMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, CopyMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, PasteMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, DeleteMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, RenameMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, CopyResourceKeyMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, CopyFilePathMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, OpenFileExplorerMenuOption>();
        services.AddSingleton<IMenuOption<ExplorerMenuContext>, OpenApplicationMenuOption>();
    }
}
