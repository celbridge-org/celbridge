using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a document in the editor.
/// </summary>
public class OpenMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public int Priority => 2;
    public string GroupId => nameof(ExplorerMenuGroup.DocumentActions);

    public OpenMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        var label = _stringLocalizer.GetString("ResourceTree_Open");
        return new MenuItemDisplayInfo(label);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        if (context.ClickedResource.IsFolder)
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        return new MenuItemState(IsVisible: true, IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.ClickedResource.IsFolder)
        {
            return;
        }

        var resourceKey = context.ClickedResource.Resource;

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
        });
    }
}
