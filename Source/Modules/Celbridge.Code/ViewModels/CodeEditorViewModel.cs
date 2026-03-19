using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Code.ViewModels;

public partial class CodeEditorViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IDocumentsService _documentsService;

    // A cache of the editor text that was last saved to disk.
    // This is the text that is displayed in the preview panel.
    [ObservableProperty]
    private string _cachedText = string.Empty;

    public CodeEditorViewModel(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        EnableFileChangeMonitoring();
    }

    public async Task<Result<string>> LoadDocument()
    {
        var result = await LoadTextFromFileAsync();
        if (result.IsSuccess)
        {
            CachedText = result.Value;
        }
        return result;
    }

    public async Task<Result> SaveDocumentContent(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        CachedText = text;

        return await SaveTextToFileAsync(text);
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public void ToggleLayout()
    {
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = WindowModeTransition.ToggleZenMode;
        });
    }

    public void NavigateToURL(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            // Navigating to an empty URL is a no-op
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
    }
}
