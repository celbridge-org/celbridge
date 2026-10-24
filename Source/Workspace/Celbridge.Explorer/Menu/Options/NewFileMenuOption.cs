using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to add a new file to a folder.
/// </summary>
public class NewFileMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.AddItems);

    public NewFileMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        var label = _stringLocalizer.GetString("ResourceTree_NewFile");
        return new MenuItemDisplayInfo(
            LocalizedText: label,
            Icon: IconSymbol.FileAdd);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrProjectFolderTargeted,
            IsEnabled: context.CanAddToTargetFolder);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var destFolderKey = context.GetTargetFolder();

        _commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = destFolderKey;
        });
    }
}
