using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.DataTransfer;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to copy resources to clipboard.
/// </summary>
public class CopyMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 2;
    public string GroupId => ExplorerMenuGroups.Clipboard;

    public CopyMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_Copy"),
            IconGlyph: "\uE8C8"); // Copy icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var canCopy = context.HasAnySelection && !context.SelectionContainsRootFolder;
        return new MenuItemState(IsVisible: true, IsEnabled: canCopy);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!context.HasAnySelection || context.SelectionContainsRootFolder)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var commandService = _commandService;

        var resourceKeys = context.SelectedResources
            .Select(r => resourceRegistry.GetResourceKey(r))
            .ToList();

        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Copy;
        });
    }
}
