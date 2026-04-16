using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to open a document with a specific editor chosen by the user.
/// Only visible when multiple editors are registered for the file's extension.
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

        var extension = Path.GetExtension(clickedFile.Name).ToLowerInvariant();
        var registry = _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry;
        var factories = registry.GetFactoriesForFileExtension(extension);

        bool hasMultipleEditors = factories.Count >= 2;
        return new MenuItemState(IsVisible: hasMultipleEditors, IsEnabled: hasMultipleEditors);
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
        var editorRegistry = _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry;
        var factories = editorRegistry.GetFactoriesForFileExtension(extension);

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

        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);

        if (currentEditorId.IsEmpty)
        {
            var preferredId = await workspaceSettings.GetPropertyAsync<string>(preferenceKey);
            // Use TryParse rather than the throwing constructor: a persisted preference may
            // reference an editor that has been renamed or uninstalled.
            if (!string.IsNullOrEmpty(preferredId) && DocumentEditorId.TryParse(preferredId, out var parsedPreferredId))
            {
                currentEditorId = parsedPreferredId;
            }
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

        var choiceResult = await _dialogService.ShowChoiceDialogAsync(title, message, displayNames, defaultIndex, checkbox);
        if (choiceResult.IsFailure)
        {
            return;
        }

        var selectedFactory = factories[choiceResult.Value.SelectedIndex];

        if (choiceResult.Value.CheckboxChecked)
        {
            await workspaceSettings.SetPropertyAsync(preferenceKey, selectedFactory.EditorId.ToString());
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
            command.EditorId = selectedFactory.EditorId;
        });
    }
}
