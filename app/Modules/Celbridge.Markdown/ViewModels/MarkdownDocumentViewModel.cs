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
    private readonly IMessengerService _messengerService;

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
        _messengerService = messengerService;

        // Register for resource change messages
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    partial void OnViewModeChanged(MarkdownViewMode value)
    {
        ViewModeChanged?.Invoke(this, value);
    }

    private void OnMonitoredResourceChangedMessage(object recipient, MonitoredResourceChangedMessage message)
    {
        if (message.Resource == FileResource)
        {
            // Skip reload if we're currently saving - this is our own file change
            if (IsSavingFile)
            {
                return;
            }

            if (IsFileChangedExternally())
            {
                RaiseReloadRequested();
            }
        }
    }

    private void OnDocumentSaveCompletedMessage(object recipient, DocumentSaveCompletedMessage message)
    {
        if (message.DocumentResource == FileResource)
        {
            UpdateFileTrackingInfo();
        }
    }

    public async Task<Result<string>> LoadDocument()
    {
        try
        {
            // Read the file contents
            var text = await File.ReadAllTextAsync(FilePath);

            // Track the initial file state when loading
            UpdateFileTrackingInfo();

            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to load markdown file: '{FilePath}'")
                .WithException(ex);
        }
    }

    public async Task<Result> SaveDocumentContent(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        try
        {
            // Set flag to suppress reload requests triggered by our own save
            IsSavingFile = true;

            await File.WriteAllTextAsync(FilePath, text);

            // Update tracking info to avoid false external change detection
            // when file watcher events arrive
            UpdateFileTrackingInfo();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save markdown file: '{FilePath}'")
                .WithException(ex);
        }
        finally
        {
            IsSavingFile = false;
        }

        return Result.Ok();
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public override void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
