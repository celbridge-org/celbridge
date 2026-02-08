using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to copy resource key to clipboard.
/// </summary>
public class CopyResourceKeyMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => ExplorerMenuGroups.Utilities;

    public CopyResourceKeyMenuOption(
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
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_CopyResourceKey"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var canCopy = context.HasSingleSelection && !context.SelectionContainsRootFolder;
        return new MenuItemState(
            IsVisible: context.HasSingleSelection,
            IsEnabled: canCopy);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!context.HasSingleSelection || context.SelectionContainsRootFolder)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(context.SingleSelectedResource!);

        _commandService.Execute<ICopyResourceKeyCommand>(command =>
        {
            command.ResourceKey = resourceKey;
        });
    }
}

