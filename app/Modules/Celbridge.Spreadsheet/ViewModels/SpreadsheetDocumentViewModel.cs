using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Spreadsheet.ViewModels;

public partial class SpreadsheetDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private string _source = string.Empty;

    public SpreadsheetDocumentViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        // Register for resource change messages
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    private void OnMonitoredResourceChangedMessage(object recipient, MonitoredResourceChangedMessage message)
    {
        // Check if the changed resource is the current document
        if (message.Resource == FileResource)
        {
            // Skip reload if we're currently saving - this is our own file change
            if (IsSavingFile)
            {
                return;
            }

            // Check if this change is genuinely different from our last save
            if (IsFileChangedExternally())
            {
                // This is an external change, notify the view to reload
                RaiseReloadRequested();
            }
        }
    }

    private void OnDocumentSaveCompletedMessage(object recipient, DocumentSaveCompletedMessage message)
    {
        // Check if this is a save completion for the current document
        if (message.DocumentResource == FileResource)
        {
            // Update our tracking information after a successful save
            UpdateFileTrackingInfo();
        }
    }

    public async Task<Result> LoadContent()
    {
        try
        {
            // Track the initial file state when loading
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

    public async Task<Result> SaveDocument()
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        // The actual saving is handled in SpreadsheetDocumentView
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> SaveSpreadsheetDataToFile(string spreadsheetData)
    {
        try
        {
            // Set flag before writing to suppress file watcher reload requests
            IsSavingFile = true;

            byte[] fileBytes = Convert.FromBase64String(spreadsheetData);

            await File.WriteAllBytesAsync(FilePath, fileBytes);

            // Update file tracking info immediately after writing
            UpdateFileTrackingInfo();

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save Excel file: '{FilePath}'")
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
        // Unregister message handlers
        _messengerService.UnregisterAll(this);
    }
}
