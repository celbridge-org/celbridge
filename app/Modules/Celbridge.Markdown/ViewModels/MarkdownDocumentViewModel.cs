using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Markdown.ViewModels;

/// <summary>
/// View model for markdown document editing with Monaco editor.
/// Handles file I/O, document state management, and preview visibility.
/// </summary>
public partial class MarkdownDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    /// <summary>
    /// Whether the preview panel is currently visible.
    /// </summary>
    [ObservableProperty]
    private bool _isPreviewVisible;

    /// <summary>
    /// Event raised when preview visibility changes.
    /// </summary>
    public event EventHandler<bool>? PreviewVisibilityChanged;

    public MarkdownDocumentViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        // Register for resource change messages
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    partial void OnIsPreviewVisibleChanged(bool value)
    {
        PreviewVisibilityChanged?.Invoke(this, value);
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

    public async Task<Result> SaveDocument(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        try
        {
            // Set flag to suppress reload requests triggered by our own save
            IsSavingFile = true;

            await File.WriteAllTextAsync(FilePath, text);

            // Update tracking info BEFORE sending completion message to avoid race condition
            // with file watcher events that may arrive before the message is processed
            UpdateFileTrackingInfo();

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);
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
