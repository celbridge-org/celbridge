using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to add a new folder.
/// </summary>
public class AddFolderMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 2;
    public string GroupId => ExplorerMenuGroups.AddItems;

    public AddFolderMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_AddFolder"),
            IconGlyph: "\uE8B7"); // Folder icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrRootTargeted,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var destFolder = context.GetTargetFolder();
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var destFolderKey = resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestFolderResource = destFolderKey;
        });
    }
}
