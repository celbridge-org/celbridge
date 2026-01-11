using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Security.Cryptography;

namespace Celbridge.Documents.ViewModels;

public partial class MonacoEditorViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;

    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private double _saveTimer;

    // A cache of the editor text that was last saved to disk.
    // This is the text that is displayed in the preview panel.
    [ObservableProperty]
    private string _cachedText = string.Empty;

    // Track the hash and size of the last saved file to detect genuine external changes
    private string? _lastSavedFileHash = null;
    private long _lastSavedFileSize = 0;

    // Event to notify the view that the document should be reloaded
    public event EventHandler? ReloadRequested;

    public MonacoEditorViewModel(
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _messengerService = messengerService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        // Register for resource change messages
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    public async Task<Result<string>> LoadDocument()
    {
        try
        {
            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(FilePath);

            CachedText = text;

            // Track the initial file state when loading
            UpdateFileTrackingInfo();

            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to load document file: '{FilePath}'")
                .WithException(ex);
        }
    }

    public async Task<Result> SaveDocument(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        CachedText = text;

        try
        {
            await File.WriteAllTextAsync(FilePath, text);

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save document file: '{FilePath}'")
                .WithException(ex);
        }

        return Result.Ok();
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

    public string GetDocumentLanguage()
    {
        return _documentsService.GetDocumentLanguage(FileResource);
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public void ToggleAllPanels()
    {
        _commandService.Execute<IToggleZenModeCommand>();
    }

    public void NavigateToURL(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            // Navigating to an empty URL is a no-op
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });
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

            // If file size is different, it's definitely changed
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

    public void Cleanup()
    {
        // Unregister message handlers
        _messengerService.UnregisterAll(this);
    }
}
