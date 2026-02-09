using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.DataTransfer;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to paste resources from clipboard.
/// </summary>
public class PasteMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 3;
    public string GroupId => ExplorerMenuGroups.Clipboard;

    public PasteMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_Paste"),
            IconGlyph: "\uE77F"); // Paste icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrRootTargeted,
            IsEnabled: context.HasClipboardData);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!context.HasClipboardData)
        {
            return;
        }

        var destFolder = context.GetTargetFolder();
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var destFolderKey = resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderKey;
        });
    }
}
