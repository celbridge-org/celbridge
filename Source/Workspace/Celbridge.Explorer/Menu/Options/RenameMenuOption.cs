using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
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
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

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
            Icon: IconSymbol.Rename);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        bool isVisible = context.ClickedResource != null;

        // Cannot rename the project folder (whether clicked directly or in selection)
        bool canRename = context.ClickedResource != null
            && !context.IsProjectFolderTargeted
            && !context.SelectionContainsProjectFolder;
        if (!canRename)
        {
            return new MenuItemState(IsVisible: isVisible, IsEnabled: false);
        }

        if (!context.CanModifySelection)
        {
            return new MenuItemState(IsVisible: isVisible, IsEnabled: false);
        }

        return new MenuItemState(IsVisible: isVisible, IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.ClickedResource == null || context.SelectionContainsProjectFolder)
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
