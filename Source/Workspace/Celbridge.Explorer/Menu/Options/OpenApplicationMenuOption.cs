using Celbridge.Commands;
using Celbridge.ContextMenu;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a file resource in its default application.
/// </summary>
public class OpenApplicationMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 2;
    public string GroupId => nameof(ExplorerMenuGroup.FileSystem);

    public OpenApplicationMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_OpenApplication"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        var isFile = !context.ClickedResource.IsFolder;
        return new MenuItemState(
            IsVisible: true,
            IsEnabled: isFile);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.ClickedResource.IsFolder)
        {
            return;
        }

        var resourceKey = context.ClickedResource.Resource;

        _commandService.Execute<IOpenApplicationCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
