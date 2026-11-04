using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option that lists the editors able to open the clicked file in a submenu, with a check against
/// the file's current editor. Choosing an editor records the choice beside the file and opens the file in
/// the chosen editor.
/// </summary>
public class OpenWithMenuOption : IMenuOption<ExplorerMenuContext>, ISubMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<OpenWithMenuOption> _logger;

    public int Priority => 3;
    public string GroupId => nameof(ExplorerMenuGroup.DocumentActions);

    public OpenWithMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        ILogger<OpenWithMenuOption> logger)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        var label = _stringLocalizer.GetString("Explorer_OpenWith");
        return new MenuItemDisplayInfo(label);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        if (context.ClickedResource.IsFolder)
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        var candidates = GetCandidateFactories(context.ClickedResource.Resource);

        bool hasMultipleEditors = candidates.Count >= 2;
        return new MenuItemState(IsVisible: hasMultipleEditors, IsEnabled: hasMultipleEditors);
    }

    private IReadOnlyList<IDocumentEditorFactory> GetCandidateFactories(ResourceKey clickedFile)
    {
        var registry = _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry;

        return registry.GetUserPickableFactoriesForResource(clickedFile);
    }

    public IReadOnlyList<SubMenuItem> GetSubMenuItems(ExplorerMenuContext context)
    {
        if (context.ClickedResource.IsFolder)
        {
            return Array.Empty<SubMenuItem>();
        }

        var fileResource = context.ClickedResource.Resource;

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var pickList = documentsService.GetEditorPickList(fileResource, context.ClickedFileEditorId);
        if (pickList is null)
        {
            return Array.Empty<SubMenuItem>();
        }

        var items = new List<SubMenuItem>();
        for (int i = 0; i < pickList.EditorIds.Count; i++)
        {
            var editorId = pickList.EditorIds[i];

            var item = new SubMenuItem(
                pickList.Labels[i],
                Icon: null,
                Execute: () => OpenWithEditor(fileResource, editorId),
                IsChecked: i == pickList.CurrentIndex);

            items.Add(item);
        }

        return items;
    }

    // The option shows only when two or more editors can open the file, and the builder then shows the
    // submenu. The flat item that would run this method is never built.
    public void Execute(ExplorerMenuContext context)
    {
    }

    private async void OpenWithEditor(ResourceKey fileResource, EditorId editorId)
    {
        try
        {
            if (!_workspaceWrapper.IsWorkspaceLoaded)
            {
                return;
            }

            var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

            var setResult = await documentsService.SetPreferredEditorAsync(fileResource, editorId);
            if (setResult.IsFailure)
            {
                _logger.LogWarning(setResult, $"Failed to record the editor choice for '{fileResource}'");
            }

            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = fileResource;
                command.EditorId = editorId;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open With menu option failed");
        }
    }
}
