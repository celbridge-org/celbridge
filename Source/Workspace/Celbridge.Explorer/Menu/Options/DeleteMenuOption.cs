using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to delete resources.
/// </summary>
public class DeleteMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 4;
    public string GroupId => ExplorerMenuGroups.Clipboard;

    public DeleteMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_Delete"),
            IconGlyph: "\uE74D"); // Delete icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var canDelete = context.HasAnySelection && !context.SelectionContainsRootFolder;
        return new MenuItemState(IsVisible: true, IsEnabled: canDelete);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!context.HasAnySelection || context.SelectionContainsRootFolder)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKeys = context.SelectedResources
            .Select(r => resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = resourceKeys;
        });
    }
}
