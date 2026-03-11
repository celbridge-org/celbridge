using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Markdown.ViewModels;

/// <summary>
/// Represents the active view mode for a markdown document.
/// </summary>
public enum MarkdownViewMode
{
    Preview,
    Split,
    Source
}

/// <summary>
/// View model for markdown document editing with Monaco editor.
/// Handles file I/O, document state management, and view mode.
/// </summary>
public partial class MarkdownDocumentViewModel : DocumentViewModel
{
    /// <summary>
    /// The current view mode of the document.
    /// </summary>
    [ObservableProperty]
    private MarkdownViewMode _viewMode = MarkdownViewMode.Preview;

    /// <summary>
    /// Event raised when the view mode changes.
    /// </summary>
    public event EventHandler<MarkdownViewMode>? ViewModeChanged;

    public MarkdownDocumentViewModel(IMessengerService messengerService)
    {
        EnableFileChangeMonitoring(messengerService);
    }

    partial void OnViewModeChanged(MarkdownViewMode value)
    {
        ViewModeChanged?.Invoke(this, value);
    }

    public async Task<Result<string>> LoadDocument()
    {
        return await LoadTextFromFileAsync();
    }

    public async Task<Result> SaveDocumentContent(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        return await SaveTextToFileAsync(text);
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }
}
