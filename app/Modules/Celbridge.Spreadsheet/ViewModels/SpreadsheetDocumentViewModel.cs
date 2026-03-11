using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
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

        EnableFileChangeMonitoring(messengerService);
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
}
