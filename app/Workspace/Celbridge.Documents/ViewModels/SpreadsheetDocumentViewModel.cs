using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class SpreadsheetDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private double _saveTimer;

    public SpreadsheetDocumentViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void OnDataChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        if (!HasUnsavedChanges)
        {
            return Result<bool>.Fail($"Document does not have unsaved changes: {FileResource}");
        }

        if (SaveTimer > 0)
        {
            SaveTimer -= deltaTime;
            if (SaveTimer <= 0)
            {
                SaveTimer = 0;
                return Result<bool>.Ok(true);
            }
        }

        return Result<bool>.Ok(false);
    }

    public async Task<Result> LoadContent()
    {
        try
        {
            var fileUri = new Uri(FilePath);
            Source = fileUri.ToString();
            await Task.CompletedTask;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occured when loading document from file: {FilePath}")
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
            byte[] fileBytes = Convert.FromBase64String(spreadsheetData);
            
            await File.WriteAllBytesAsync(FilePath, fileBytes);

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save Excel file: '{FilePath}'")
                .WithException(ex);
        }
    }
}
