using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Platform;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a resource in the platform's system file manager.
/// </summary>
public class OpenFileExplorerMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IPlatformInfo _platformInfo;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.FileSystem);

    public OpenFileExplorerMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IPlatformInfo platformInfo)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
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
        var resourceKey = context.ClickedResource.Resource;

        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
