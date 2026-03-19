using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.DataTransfer;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to copy the resource's path to clipboard.
/// </summary>
public class CopyPathMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => ExplorerMenuGroups.Utilities;

    public CopyPathMenuOption(
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
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_CopyPath"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrRootTargeted,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var target = context.ClickedResource ?? context.RootFolder;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(target);
        var filePath = Path.Combine(resourceRegistry.ProjectFolderPath, resourceKey.ToString());

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = filePath;
        });
    }
}

