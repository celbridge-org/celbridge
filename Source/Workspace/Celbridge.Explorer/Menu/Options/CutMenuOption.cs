using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Celbridge.DataTransfer;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to cut resources to clipboard.
/// </summary>
public class CutMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public CutMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_Cut"),
            Icon: IconSymbol.Cut); // scissors (cut) icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        // Cut implies a later move, so a locked or path-frozen resource cannot be
        // cut. Copy stays available because copying never modifies the source.
        if (!context.HasAnySelection
            || context.SelectionContainsProjectFolder)
        {
            return new MenuItemState(IsVisible: true, IsEnabled: false);
        }

        if (!context.CanModifySelection)
        {
            return new MenuItemState(IsVisible: true, IsEnabled: false);
        }

        return new MenuItemState(IsVisible: true, IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!context.HasAnySelection || context.SelectionContainsProjectFolder)
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
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
