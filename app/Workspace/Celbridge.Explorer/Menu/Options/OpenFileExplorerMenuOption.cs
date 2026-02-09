using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a resource in Windows File Explorer.
/// </summary>
public class OpenFileExplorerMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => ExplorerMenuGroups.FileSystem;

    public OpenFileExplorerMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_OpenFileExplorer"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrRootTargeted,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var target = context.ClickedResource ?? context.RootFolder;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(target);

        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
