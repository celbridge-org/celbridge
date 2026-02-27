using Celbridge.Messaging;

namespace Celbridge.Documents.ViewModels;

public partial class MarkdownDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    public MarkdownDocumentViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
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

    public async Task<Result> LoadContent()
    {
        try
        {
            UpdateFileTrackingInfo();

            await Task.CompletedTask;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading document from file: {FilePath}")
                .WithException(ex);
        }
    }

    public async Task<string> LoadMarkdownContent()
    {
        if (!File.Exists(FilePath))
        {
            return string.Empty;
        }

        var content = await File.ReadAllTextAsync(FilePath);
        return content ?? string.Empty;
    }

    public async Task<Result> SaveDocument()
    {
        HasUnsavedChanges = false;
        SaveTimer = 0;

        // The actual saving is handled in MarkdownDocumentView
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> SaveMarkdownToFile(string markdownContent)
    {
        try
        {
            // Set flag before writing to suppress file watcher reload requests
            IsSavingFile = true;

            await File.WriteAllTextAsync(FilePath, markdownContent);

            // Update file tracking info immediately after writing
            UpdateFileTrackingInfo();

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save markdown file: '{FilePath}'")
                .WithException(ex);
        }
        finally
        {
            // Clear the flag after save completes (success or failure)
            IsSavingFile = false;
        }
    }

    public override void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
