using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to add a new file to a folder.
/// </summary>
public class AddFileMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => ExplorerMenuGroups.AddItems;

    public AddFileMenuOption(
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
        var label = _stringLocalizer.GetString("ResourceTree_AddFile");
        return new MenuItemDisplayInfo(
            LocalizedText: label,
            IconGlyph: "\uE8A5"); // Page2 icon
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsSingleItemOrRootTargeted,
            IsEnabled: true);
    }

    public void Execute(ExplorerMenuContext context)
    {
        var destFolder = context.GetTargetFolder();
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var destFolderKey = resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = destFolderKey;
        });
    }
}
