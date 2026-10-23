using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to rename a resource.
/// </summary>
public class RenameMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IShortcutHintService _shortcutHintService;

    public int Priority => 5;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public RenameMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IShortcutHintService shortcutHintService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _shortcutHintService = shortcutHintService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(
            _stringLocalizer.GetString("ResourceTree_Rename"),
            Icon: IconSymbol.Rename,
            ShortcutHint: _shortcutHintService.GetText(EditIntent.Rename));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        // Cannot rename the project folder (whether clicked directly or in selection)
        bool canRename = !context.IsProjectFolderTargeted
            && !context.SelectionContainsProjectFolder;
        if (!canRename)
        {
            return new MenuItemState(IsVisible: true, IsEnabled: false);
        }

        if (!context.CanModifySelection)
        {
            return new MenuItemState(IsVisible: true, IsEnabled: false);
        }

        return new MenuItemState(IsVisible: true, IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.IsProjectFolderTargeted
            || context.SelectionContainsProjectFolder)
        {
            return;
        }

        var resourceKey = context.ClickedResource.Resource;

        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
