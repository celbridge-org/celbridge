using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Security.Cryptography;

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

    // Track the hash and size of the last saved file to detect genuine external changes
    private string? _lastSavedFileHash = null;
    private long _lastSavedFileSize = 0;

    // Event to notify the view that the spreadsheet should be reloaded
    public event EventHandler? ReloadRequested;

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
            // Check if this change is genuinely different from our last save
            if (IsFileChangedExternally())
            {
                // This is an external change, notify the view to reload
                ReloadRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool IsFileChangedExternally()
    {
        // If we haven't saved yet, any change is considered external
        if (_lastSavedFileHash == null)
        {
            return true;
        }

        try
        {
            if (!File.Exists(FilePath))
            {
                // File was deleted, consider this an external change
                return true;
            }

            var fileInfo = new FileInfo(FilePath);
            var currentSize = fileInfo.Length;

            // Quick check: if file size is different, it's definitely changed
            if (currentSize != _lastSavedFileSize)
            {
                return true;
            }

            // File size is the same - compute hash to check if content actually changed
            // This handles cases where the file was rewritten with identical content
            var currentHash = ComputeFileHash(FilePath);
            
            return currentHash != _lastSavedFileHash;
        }
        catch (Exception)
        {
            // If we can't read the file, assume it changed (safer to reload)
            return true;
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hashBytes);
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

    private void UpdateFileTrackingInfo()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var fileInfo = new FileInfo(FilePath);
                _lastSavedFileSize = fileInfo.Length;
                _lastSavedFileHash = ComputeFileHash(FilePath);
            }
        }
        catch (Exception)
        {
            // If we can't read the file, clear our tracking info
            _lastSavedFileHash = null;
            _lastSavedFileSize = 0;
        }
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

            // Track the initial file state when loading
            UpdateFileTrackingInfo();

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

    public void Cleanup()
    {
        // Unregister message handlers
        _messengerService.UnregisterAll(this);
    }
}
