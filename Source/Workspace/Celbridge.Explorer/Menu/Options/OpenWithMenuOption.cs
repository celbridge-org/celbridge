using Celbridge.ContextMenu;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option that lists the editors able to open the clicked file in a submenu, with a check against
/// the file's current editor. Choosing an editor opens the file in the chosen editor and records the
/// choice beside the file.
/// </summary>
public class OpenWithMenuOption : IMenuOption<ExplorerMenuContext>, ISubMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<OpenWithMenuOption> _logger;

    public int Priority => 3;
    public string GroupId => nameof(ExplorerMenuGroup.DocumentActions);

    public OpenWithMenuOption(
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper,
        ILogger<OpenWithMenuOption> logger)
    {
        _stringLocalizer = stringLocalizer;
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
        bool hasChoices = context.OpenWithChoices is not null;
        return new MenuItemState(IsVisible: hasChoices, IsEnabled: hasChoices);
    }

    public IReadOnlyList<SubMenuItem> GetSubMenuItems(ExplorerMenuContext context)
    {
        var choices = context.OpenWithChoices;
        if (choices is null)
        {
            return Array.Empty<SubMenuItem>();
        }

        var fileResource = context.ClickedResource.Resource;

        var items = new List<SubMenuItem>();
        for (int i = 0; i < choices.EditorIds.Count; i++)
        {
            var editorId = choices.EditorIds[i];

            var item = new SubMenuItem(
                choices.Labels[i],
                Icon: null,
                Execute: () => OpenWithEditor(fileResource, editorId),
                IsChecked: i == choices.CurrentIndex);

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

            var openResult = await documentsService.OpenWithEditorAsync(fileResource, editorId);
            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open '{fileResource}' in editor '{editorId}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to open '{fileResource}' in editor '{editorId}'");
        }
    }
}
