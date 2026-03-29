using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to create a zip archive from a folder.
/// </summary>
public class ArchiveMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 6;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public ArchiveMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_CreateArchive"),
            IconGlyph: "\uE8B7");
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var isSingleFolder = context.HasSingleSelection &&
                             context.SingleSelectedResource is IFolderResource &&
                             !context.SelectionContainsRootFolder;

        return new MenuItemState(
            IsVisible: isSingleFolder,
            IsEnabled: isSingleFolder);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.SingleSelectedResource is not IFolderResource)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(context.SingleSelectedResource);

        _commandService.Execute<IArchiveResourceDialogCommand>(command =>
        {
            command.FolderResource = resourceKey;
        });
    }
}
