using System.Security.Cryptography;
using System.Text;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public abstract partial class DocumentViewModel : ObservableObject
{
    // Delay before saving the document after the most recent change
    protected const double SaveDelay = 1.0; // Seconds

    private IMessengerService? _messengerService;
    private ILogger<DocumentViewModel>? _logger;

    [ObservableProperty]
    private ResourceKey _fileResource = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges = false;

    [ObservableProperty]
    private double _saveTimer;

    // Event to notify the view that the document should be reloaded
    public event EventHandler? ReloadRequested;

    // Track the hash and size of the last saved file to detect genuine external changes
    private string? _lastSavedFileHash;
    private long _lastSavedFileSize;

    /// <summary>
    /// Marks the document as having unsaved changes and resets the save timer.
    /// </summary>
    public virtual void OnDataChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    /// <summary>
    /// Updates the save timer and returns true when the timer expires and a save should occur.
    /// </summary>
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

    /// <summary>
    /// Raises the ReloadRequested event to notify the view that the document should be reloaded.
    /// </summary>
    protected void RaiseReloadRequested()
    {
        ReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enables file-change monitoring for this document.
    /// Registers for MonitoredResourceChangedMessage and DocumentSaveCompletedMessage.
    /// Call this in the ViewModel constructor for editors that need external file change detection.
    /// </summary>
    protected void EnableFileChangeMonitoring()
    {
        Guard.IsNull(_messengerService, "File change monitoring is already enabled");

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        try
        {
            _logger = ServiceLocator.AcquireService<ILogger<DocumentViewModel>>();
        }
        catch
        {
            // Logger may not be available in test environments
        }

        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChanged);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompleted);
    }

    private void OnMonitoredResourceChanged(object recipient, MonitoredResourceChangedMessage message)
    {
        if (message.Resource != FileResource)
        {
            return;
        }

        // Self-events from our own writes hash-match _lastSavedFileHash and are
        // ignored. Genuine external changes have a different hash and proceed.
        if (IsFileChangedExternally())
        {
            // External edits supersede any pending or in-flight buffer save.
            // Discard the queued save so the buffer reload wins.
            SaveTimer = 0;
            HasUnsavedChanges = false;

            _logger?.LogDebug($"External change detected for '{FileResource}', requesting reload");
            RaiseReloadRequested();
        }
    }

    private void OnDocumentSaveCompleted(object recipient, DocumentSaveCompletedMessage message)
    {
        if (message.DocumentResource == FileResource)
        {
            UpdateFileTrackingInfo();
        }
    }

    /// <summary>
    /// Loads text content from the file at FilePath.
    /// Updates file tracking info after a successful load.
    /// </summary>
    protected async Task<Result<string>> LoadTextFromFileAsync()
    {
        try
        {
            var text = await File.ReadAllTextAsync(FilePath);
            UpdateFileTrackingInfo();
            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to load file: '{FilePath}'")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Saves text content as UTF-8 (no BOM) to the file at FilePath.
    /// Callers are responsible for managing HasUnsavedChanges and SaveTimer.
    /// </summary>
    protected async Task<Result> SaveTextToFileAsync(string text)
    {
        var intendedBytes = Encoding.UTF8.GetBytes(text);
        return await SaveBytesToFileAsync(intendedBytes);
    }

    /// <summary>
    /// Decodes base64 content and saves the raw bytes to the file at FilePath.
    /// Callers are responsible for managing HasUnsavedChanges and SaveTimer.
    /// Returns failure if the content is not valid base64.
    /// </summary>
    protected async Task<Result> SaveBinaryToFileAsync(string base64Content)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Content);
        }
        catch (FormatException)
        {
            return Result.Fail($"Invalid base64 content when saving binary file: '{FilePath}'");
        }

        return await SaveBytesToFileAsync(bytes);
    }

    /// <summary>
    /// Routes the save through IResourceFileWriter (atomic write + bounded retry
    /// on transient IO) and raises ReloadRequested when external interleaving is
    /// detected either before the write (pre-write hash check) or between the
    /// write completing and our tracking-hash read (post-write check). Updates
    /// file tracking info on a successful write.
    /// </summary>
    private async Task<Result> SaveBytesToFileAsync(byte[] bytes)
    {
        var intendedHash = ComputeBytesHash(bytes);

        if (TryDetectPreWriteExternalChange())
        {
            return Result.Ok();
        }

        var writer = GetFileWriter();
        var writeResult = await writer.WriteAllBytesAsync(FileResource, bytes);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        UpdateFileTrackingInfo();

        if (_lastSavedFileHash is not null
            && _lastSavedFileHash != intendedHash)
        {
            _logger?.LogDebug($"External write interleaved with save for '{FileResource}', requesting reload");
            RaiseReloadRequested();
        }

        return Result.Ok();
    }

    /// <summary>
    /// Reads the current disk hash and compares it to the last-tracked save hash.
    /// If the disk has drifted, discards any buffered changes, aligns tracking
    /// with the current disk state (so the upcoming watcher event filters as a
    /// self-event), raises ReloadRequested, and returns true. Returns false if
    /// no drift is detected, if there is no prior tracking info to compare
    /// against, or if the disk read fails (the caller falls through to the
    /// write attempt, whose retry loop handles transient IO errors).
    /// </summary>
    private bool TryDetectPreWriteExternalChange()
    {
        if (_lastSavedFileHash is null
            || !File.Exists(FilePath))
        {
            return false;
        }

        try
        {
            var preWriteHash = ComputeFileHash(FilePath);
            if (preWriteHash == _lastSavedFileHash)
            {
                return false;
            }

            SaveTimer = 0;
            HasUnsavedChanges = false;
            UpdateFileTrackingInfo();

            _logger?.LogDebug($"External write detected before save for '{FileResource}', aborting save and requesting reload");
            RaiseReloadRequested();

            return true;
        }
        catch (IOException ex)
        {
            _logger?.LogDebug(ex, $"Pre-write hash check failed for '{FilePath}', proceeding to write attempt");
            return false;
        }
    }

    /// <summary>
    /// Acquires the resource file writer. Overridable so tests can substitute
    /// a writer wired to a temp folder without going through the workspace
    /// service hierarchy.
    /// </summary>
    protected virtual IResourceFileWriter GetFileWriter()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        return workspaceWrapper.WorkspaceService.ResourceService.FileWriter;
    }

    /// <summary>
    /// Override this method to perform cleanup when the document is closed.
    /// Always call base.Cleanup() to ensure file-change monitoring is properly unregistered.
    /// </summary>
    public virtual void Cleanup()
    {
        _messengerService?.UnregisterAll(this);
    }

    protected bool IsFileChangedExternally()
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

    protected virtual void UpdateFileTrackingInfo()
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

    protected static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hashBytes);
    }

    private static string ComputeBytesHash(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }
}
