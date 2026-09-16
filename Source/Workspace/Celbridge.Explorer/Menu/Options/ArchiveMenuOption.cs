using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
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
            Icon: IconSymbol.Archive);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var hasTargetFolder = GetTargetFolder(context) is not null;

        return new MenuItemState(
            IsVisible: hasTargetFolder,
            IsEnabled: hasTargetFolder);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var targetFolder = GetTargetFolder(context);
        if (targetFolder is null)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(targetFolder);

        _commandService.Execute<IArchiveResourceDialogCommand>(command =>
        {
            command.FolderResource = resourceKey;
        });
    }

    private static IFolderResource? GetTargetFolder(ExplorerMenuContext context)
    {
        if (context.IsProjectFolderTargeted)
        {
            return context.ProjectFolder;
        }

        return context.SingleSelectedResource as IFolderResource;
    }
}
