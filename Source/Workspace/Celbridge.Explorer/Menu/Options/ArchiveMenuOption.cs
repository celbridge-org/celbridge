using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to create a zip archive from a folder.
/// </summary>
public class ArchiveMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 6;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public ArchiveMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(
            _stringLocalizer.GetString("ResourceTree_CreateArchive"),
            Icon: IconSymbol.Archive);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var selectedResource = context.SingleSelectedResource;
        var isSingleFolder = selectedResource is not null
            && selectedResource.IsFolder
            && !context.SelectionContainsProjectFolder;

        return new MenuItemState(
            IsVisible: isSingleFolder,
            IsEnabled: isSingleFolder);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var selectedResource = context.SingleSelectedResource;
        if (selectedResource is null
            || !selectedResource.IsFolder)
        {
            return;
        }

        var resourceKey = selectedResource.Resource;

        _commandService.Execute<IArchiveResourceDialogCommand>(command =>
        {
            command.FolderResource = resourceKey;
        });
    }
}
