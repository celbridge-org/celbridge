using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to add a new file to a folder.
/// </summary>
public class NewFileMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.AddItems);

    public NewFileMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        var label = _stringLocalizer.GetString("ResourceTree_NewFile");
        return new MenuItemDisplayInfo(
            LocalizedText: label,
            Icon: IconSymbol.FileAdd); // file-earmark icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrProjectFolderTargeted,
            IsEnabled: context.CanAddToTargetFolder);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var destFolder = context.GetTargetFolder();
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var destFolderKey = resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = destFolderKey;
        });
    }
}
