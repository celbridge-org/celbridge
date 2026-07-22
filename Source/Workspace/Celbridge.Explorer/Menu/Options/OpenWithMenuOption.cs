using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option that lets the user pick which editor opens the clicked file.
/// </summary>
public class OpenWithMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;
    private readonly ILogger<OpenWithMenuOption> _logger;

    public int Priority => 3;
    public string GroupId => nameof(ExplorerMenuGroup.DocumentActions);

    public OpenWithMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService,
        ILogger<OpenWithMenuOption> logger)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
        _logger = logger;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        var label = _stringLocalizer.GetString("Explorer_OpenWith");
        return new MenuItemDisplayInfo(label);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        var candidates = GetCandidateFactories(clickedFile);

        bool hasMultipleEditors = candidates.Count >= 2;
        return new MenuItemState(IsVisible: hasMultipleEditors, IsEnabled: hasMultipleEditors);
    }

    private IReadOnlyList<IDocumentEditorFactory> GetCandidateFactories(IFileResource clickedFile)
    {
        var registry = _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(clickedFile);

        return registry.GetUserPickableFactoriesForResource(resourceKey);
    }

    public async void Execute(ExplorerMenuContext context)
    {
        try
        {
            await ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open With menu option failed");
        }
    }

    private async Task ExecuteAsync(ExplorerMenuContext context)
    {
        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(clickedFile);

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        // Preselect the editor currently opening this document (if open), else the file's sidecar
        // override; GetEditorPickList falls back to the project default when that is not a candidate.
        var currentEditorId = EditorId.Empty;
        var openDocument = documentsService
            .GetOpenDocuments()
            .FirstOrDefault(document => document.FileResource == resourceKey);
        if (openDocument is not null)
        {
            currentEditorId = openDocument.EditorId;
        }
        if (currentEditorId.IsEmpty)
        {
            currentEditorId = await documentsService.GetPreferredEditorAsync(resourceKey);
        }

        var pickList = documentsService.GetEditorPickList(resourceKey, currentEditorId);
        if (pickList is null)
        {
            return;
        }

        var title = _stringLocalizer.GetString("OpenWithDialog_Title");
        var message = _stringLocalizer.GetString("OpenWithDialog_Message");
        var openButtonText = _stringLocalizer.GetString("OpenWithDialog_OpenButton");

        var choiceResult = await _dialogService.ShowChoiceDialogAsync(
            title, message, pickList.Labels, pickList.SelectedIndex, checkbox: null, primaryButtonText: openButtonText);
        if (choiceResult.IsFailure)
        {
            return;
        }

        var selectedEditorId = pickList.EditorIds[choiceResult.Value.SelectedIndex];

        await documentsService.SetPreferredEditorAsync(resourceKey, selectedEditorId);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
            command.EditorId = selectedEditorId;
        });
    }
}
