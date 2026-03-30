using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to extract a zip archive to a folder.
/// </summary>
public class UnarchiveMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 7;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public UnarchiveMenuOption(
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
            _stringLocalizer.GetString("ResourceTree_ExtractArchive"),
            IconGlyph: "\uE8C8");
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var isZipFile = context.HasSingleSelection &&
                        context.SingleSelectedResource is IFileResource &&
                        context.SingleSelectedResource.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        return new MenuItemState(
            IsVisible: isZipFile,
            IsEnabled: isZipFile);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.SingleSelectedResource is not IFileResource)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(context.SingleSelectedResource);

        _commandService.Execute<IUnarchiveResourceDialogCommand>(command =>
        {
            command.ArchiveResource = resourceKey;
        });
    }
}
