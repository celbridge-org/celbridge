using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to copy file path to clipboard.
/// </summary>
public class CopyFilePathMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 2;
    public string GroupId => ExplorerMenuGroups.Utilities;

    public CopyFilePathMenuOption(
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
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_CopyFilePath"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrRootTargeted,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var target = context.ClickedResource ?? (context.HasSingleSelection ? context.SingleSelectedResource : null) ?? context.RootFolder;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(target);

        _commandService.Execute<ICopyFilePathCommand>(command =>
        {
            command.ResourceKey = resourceKey;
        });
    }
}

