using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.DataTransfer;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to copy resource key to clipboard.
/// </summary>
public class CopyResourceKeyMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 2;
    public string GroupId => nameof(ExplorerMenuGroup.Utilities);

    public CopyResourceKeyMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_CopyResourceKey"));
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
            command.Text = resourceKey.Path;
        });
    }
}

