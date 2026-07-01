using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to add a new folder.
/// </summary>
public class NewFolderMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 2;
    public string GroupId => nameof(ExplorerMenuGroup.AddItems);

    public NewFolderMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_NewFolder"),
            Icon: IconSymbol.FolderAdd);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrProjectFolderTargeted,
            IsEnabled: context.CanAddToTargetFolder);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var destFolder = context.GetTargetFolder();
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var destFolderKey = resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestFolderResource = destFolderKey;
        });
    }
}
