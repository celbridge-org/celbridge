using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to extract a zip archive to a folder.
/// </summary>
public class UnarchiveMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 7;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public UnarchiveMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(
            _stringLocalizer.GetString("ResourceTree_ExtractArchive"),
            Icon: IconSymbol.Unarchive);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var selectedResource = context.SingleSelectedResource;
        var isZipFile = selectedResource is not null
            && !selectedResource.IsFolder
            && selectedResource.Resource.ResourceName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        return new MenuItemState(
            IsVisible: isZipFile,
            IsEnabled: isZipFile);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var selectedResource = context.SingleSelectedResource;
        if (selectedResource is null
            || selectedResource.IsFolder)
        {
            return;
        }

        var resourceKey = selectedResource.Resource;

        _commandService.Execute<IUnarchiveResourceDialogCommand>(command =>
        {
            command.ArchiveResource = resourceKey;
        });
    }
}
