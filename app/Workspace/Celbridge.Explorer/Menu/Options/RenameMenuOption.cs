using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to rename a resource.
/// </summary>
public class RenameMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 5;
    public string GroupId => ExplorerMenuGroups.Clipboard;

    public RenameMenuOption(
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
        return new MenuItemDisplayInfo(
            _stringLocalizer.GetString("ResourceTree_Rename"),
            IconGlyph: "\uE70F"); // Edit icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var canRename = context.ClickedResource != null && !context.SelectionContainsRootFolder;
        return new MenuItemState(
            IsVisible: context.ClickedResource != null,
            IsEnabled: canRename);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.ClickedResource == null || context.SelectionContainsRootFolder)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(context.ClickedResource);

        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
