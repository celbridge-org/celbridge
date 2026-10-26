using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Search;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open the Search panel on a folder.
/// </summary>
public class SearchInFolderMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 0;
    public string GroupId => nameof(ExplorerMenuGroup.Utilities);

    public SearchInFolderMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(
            _stringLocalizer.GetString("ResourceTree_SearchInFolder"),
            Icon: IconSymbol.Search);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrProjectFolderTargeted
                && context.ClickedResource.IsFolder,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        // The project folder's key is empty, which searches the whole project.
        var folder = context.ClickedResource.Resource;

        _commandService.Execute<ISearchFolderCommand>(command =>
        {
            command.Folder = folder;
        });
    }
}
