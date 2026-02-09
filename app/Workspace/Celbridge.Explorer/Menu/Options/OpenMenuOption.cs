using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Documents;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a document in the editor.
/// </summary>
public class OpenMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 2;
    public string GroupId => ExplorerMenuGroups.DocumentActions;

    public OpenMenuOption(
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
        var label = _stringLocalizer.GetString("ResourceTree_Open");
        return new MenuItemDisplayInfo(label);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(clickedFile);
        var isSupported = documentsService.IsDocumentSupported(resourceKey);
        
        return new MenuItemState(IsVisible: true, IsEnabled: isSupported);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(clickedFile);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
        });
    }
}
