using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Platform;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a resource in the platform's system file manager.
/// </summary>
public class OpenFileExplorerMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IPlatformInfo _platformInfo;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.FileSystem);

    public OpenFileExplorerMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IPlatformInfo platformInfo)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _platformInfo = platformInfo;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        string fileManagerName = _stringLocalizer.GetString(_platformInfo.FileManagerNameStringKey);
        var label = _stringLocalizer.GetString("ResourceTree_OpenFileManager", fileManagerName);
        return new MenuItemDisplayInfo(label);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrProjectFolderTargeted,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var target = context.ClickedResource ?? context.ProjectFolder;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(target);

        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
