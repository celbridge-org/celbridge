using System.Text;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;

namespace Celbridge.Notes.ViewModels;

public partial class NoteDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;
    private readonly IFileTemplateService _fileTemplateService;

    public NoteDocumentViewModel(
        IMessengerService messengerService,
        IFileTemplateService fileTemplateService)
    {
        _messengerService = messengerService;
        _fileTemplateService = fileTemplateService;

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

    public async Task<string> LoadNoteContent()
    {
        if (!File.Exists(FilePath))
        {
            var emptyContent = _fileTemplateService.GetNewFileContent(FilePath);
            return Encoding.UTF8.GetString(emptyContent);
        }

        var content = await File.ReadAllTextAsync(FilePath);
        if (string.IsNullOrEmpty(content))
        {
            var emptyContent = _fileTemplateService.GetNewFileContent(FilePath);
            return Encoding.UTF8.GetString(emptyContent);
        }

        return content;
    }

    public async Task<Result> SaveDocument()
    {
        HasUnsavedChanges = false;
        SaveTimer = 0;

        // The actual saving is handled in NoteDocumentView
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> SaveNoteToFile(string jsonContent)
    {
        try
        {
            // Set flag before writing to suppress file watcher reload requests
            IsSavingFile = true;

            await File.WriteAllTextAsync(FilePath, jsonContent);

            // Update file tracking info immediately after writing
            UpdateFileTrackingInfo();

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save note file: '{FilePath}'")
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
