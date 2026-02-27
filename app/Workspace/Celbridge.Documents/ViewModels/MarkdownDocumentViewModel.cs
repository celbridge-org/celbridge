using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class MarkdownDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private double _saveTimer;

    // Event to notify the view that the document should be reloaded
    public event EventHandler? ReloadRequested;

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
            if (IsFileChangedExternally())
            {
                ReloadRequested?.Invoke(this, EventArgs.Empty);
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
            await File.WriteAllTextAsync(FilePath, markdownContent);

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save markdown file: '{FilePath}'")
                .WithException(ex);
        }
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
