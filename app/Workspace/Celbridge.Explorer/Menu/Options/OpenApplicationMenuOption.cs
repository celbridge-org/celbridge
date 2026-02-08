using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a file resource in its default application.
/// </summary>
public class OpenApplicationMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 2;
    public string GroupId => ExplorerMenuGroups.FileSystem;

    public OpenApplicationMenuOption(
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
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_OpenApplication"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var isFile = context.HasSingleSelection && context.SingleSelectedResource is IFileResource;
        return new MenuItemState(
            IsVisible: context.HasSingleSelection,
            IsEnabled: isFile);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!context.HasSingleSelection || context.SingleSelectedResource is not IFileResource)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(context.SingleSelectedResource);

        _commandService.Execute<IOpenApplicationCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
