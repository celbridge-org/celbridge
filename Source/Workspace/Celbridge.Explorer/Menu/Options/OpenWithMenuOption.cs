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

        var extension = Path.GetExtension(clickedFile.Name).ToLowerInvariant();
        var factories = GetCandidateFactories(clickedFile);

        if (factories.Count < 2)
        {
            return;
        }

        // Pre-select the editor that's currently being used for this document (if open),
        // falling back to the workspace preference for this extension.
        var currentEditorId = DocumentEditorId.Empty;
        var openDocument = _workspaceWrapper.WorkspaceService.DocumentsService
            .GetOpenDocuments()
            .FirstOrDefault(document => document.FileResource == resourceKey);

        if (openDocument is not null)
        {
            currentEditorId = openDocument.EditorId;
        }

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        if (currentEditorId.IsEmpty)
        {
            currentEditorId = await documentsService.GetPreferredEditorAsync(resourceKey);
        }

        int defaultIndex = 0;
        if (!currentEditorId.IsEmpty)
        {
            for (int i = 0; i < factories.Count; i++)
            {
                if (factories[i].EditorId == currentEditorId)
                {
                    defaultIndex = i;
                    break;
                }
            }
        }

        var displayNames = factories.Select(factory => factory.DisplayName).ToList();

        var title = _stringLocalizer.GetString("OpenWithDialog_Title");
        var message = _stringLocalizer.GetString("OpenWithDialog_Message");
        var checkbox = new ChoiceDialogCheckbox(_stringLocalizer.GetString("OpenWithDialog_UseAsDefault"));
        var openButtonText = _stringLocalizer.GetString("OpenWithDialog_OpenButton");

        var choiceResult = await _dialogService.ShowChoiceDialogAsync(title, message, displayNames, defaultIndex, checkbox, primaryButtonText: openButtonText);
        if (choiceResult.IsFailure)
        {
            return;
        }

        var selectedFactory = factories[choiceResult.Value.SelectedIndex];

        if (choiceResult.Value.CheckboxChecked)
        {
            _commandService.Execute<ISetEditorPreferenceCommand>(command =>
            {
                command.Extension = extension;
                command.EditorId = selectedFactory.EditorId;
            });
        }

        // Persist the user's explicit per-file choice in the sidecar's editor
        // field, creating the sidecar if needed. The KISS rule: every "Open
        // With X" invocation writes the chosen editor, even when it matches
        // the per-extension default - a redundant entry is less surprising
        // than an auto-removal the user did not request.
        _commandService.Execute<ISetFieldCommand>(command =>
        {
            command.Resource = resourceKey;
            command.Field = DocumentConstants.SidecarEditorFieldName;
            command.Value = selectedFactory.EditorId;
        });

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
            command.EditorId = selectedFactory.EditorId;
        });
    }
}
