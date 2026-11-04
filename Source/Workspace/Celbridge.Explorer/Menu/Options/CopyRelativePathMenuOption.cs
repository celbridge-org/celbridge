using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.DataTransfer;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to copy the resource's path relative to the project folder to the clipboard.
/// The path uses forward slashes on every platform.
/// </summary>
public class CopyRelativePathMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.Utilities);

    public CopyRelativePathMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_CopyRelativePath"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        // Disabled for the project folder, which has an empty ResourceKey
        var canCopy = !context.ClickedResource.Resource.IsEmpty;
        return new MenuItemState(
            IsVisible: true,
            IsEnabled: canCopy);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var resourceKey = context.ClickedResource.Resource;
        if (resourceKey.IsEmpty)
        {
            return;
        }

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = resourceKey.ShortKey;
        });
    }
}

