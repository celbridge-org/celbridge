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

    // Track the size and modified-time of the last saved file so that the
    // watcher's own event for our save hash-matches the cache and short-circuits.
    // mtime + size is cheap (one stat per check) and adequate for distinguishing
    // self-events from genuine external writes; previous hash-based tracking
    // read and SHA256'd the whole file on every watcher event.
    private long _lastSavedFileSize;
    private DateTime? _lastSavedFileMtime;

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
                return true;
            }
        }

        return false;
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
    /// Registers for ResourceChangedMessage and DocumentSaveCompletedMessage.
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

        _messengerService.Register<ResourceChangedMessage>(this, OnResourceChanged);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompleted);
    }

    private async void OnResourceChanged(object recipient, ResourceChangedMessage message)
    {
        if (message.Resource != FileResource)
        {
            return;
        }

        // Self-events from our own writes match the cached size + mtime and are
        // ignored. Genuine external changes differ and proceed.
        if (await IsFileChangedExternallyAsync())
        {
            // External edits supersede any pending or in-flight buffer save.
            // Discard the queued save so the buffer reload wins.
            SaveTimer = 0;
            HasUnsavedChanges = false;

            _logger?.LogDebug($"External change detected for '{FileResource}', requesting reload");
            RaiseReloadRequested();
        }
    }

    private async void OnDocumentSaveCompleted(object recipient, DocumentSaveCompletedMessage message)
    {
        if (message.DocumentResource == FileResource)
        {
            await UpdateFileTrackingInfoAsync();
        }
    }

    /// <summary>
    /// Loads text content from the file at FilePath.
    /// Updates file tracking info after a successful load.
    /// </summary>
    protected async Task<Result<string>> LoadTextFromFileAsync()
    {
        var fileStorage = GetFileSystem();
        var readResult = await fileStorage.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to load file: '{FilePath}'")
                .WithErrors(readResult);
        }

        await UpdateFileTrackingInfoAsync();
        return readResult.Value;
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
    /// Routes the save through IFileStorage (atomic write + bounded retry on
    /// transient IO) and raises ReloadRequested when external interleaving is
    /// detected either before the write (pre-write size/mtime check) or after
    /// (post-write size mismatch against the bytes we wrote). Updates file
    /// tracking info on a successful write.
    /// </summary>
    private async Task<Result> SaveBytesToFileAsync(byte[] bytes)
    {
        if (await TryDetectPreWriteExternalChangeAsync())
        {
            return Result.Ok();
        }

        var fileStorage = GetFileSystem();
        var writeResult = await fileStorage.WriteAllBytesAsync(FileResource, bytes);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        await UpdateFileTrackingInfoAsync();

        // Post-write interleave check: if the on-disk size disagrees with what
        // we wrote, an external writer slipped in between WriteAllBytesAsync
        // returning and our cache refresh. Same-size interleaves slip past this
        // check but get picked up by the watcher's own subsequent event (which
        // will mtime-mismatch the cache and fire a reload via OnResourceChanged).
        if (_lastSavedFileSize != bytes.Length)
        {
            _logger?.LogDebug($"External write interleaved with save for '{FileResource}', requesting reload");
            RaiseReloadRequested();
        }

        return Result.Ok();
    }

    /// <summary>
    /// Reads the current disk size + mtime and compares to the last-tracked
    /// save. If the disk has drifted, discards any buffered changes, aligns
    /// tracking with the current disk state (so the upcoming watcher event
    /// filters as a self-event), raises ReloadRequested, and returns true.
    /// Returns false if no drift is detected, if there is no prior tracking
    /// info to compare against, or if the disk probe fails (the caller falls
    /// through to the write attempt, whose retry loop handles transient IO
    /// errors).
    /// </summary>
    private async Task<bool> TryDetectPreWriteExternalChangeAsync()
    {
        if (_lastSavedFileMtime is null)
        {
            return false;
        }

        var fileStorage = GetFileSystem();
        var infoResult = await fileStorage.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            _logger?.LogDebug($"Pre-write info probe failed for '{FilePath}', proceeding to write attempt");
            return false;
        }

        if (infoResult.Value.Size == _lastSavedFileSize
            && infoResult.Value.ModifiedUtc == _lastSavedFileMtime)
        {
            return false;
        }

        SaveTimer = 0;
        HasUnsavedChanges = false;
        await UpdateFileTrackingInfoAsync();

        _logger?.LogDebug($"External write detected before save for '{FileResource}', aborting save and requesting reload");
        RaiseReloadRequested();

        return true;
    }

    /// <summary>
    /// Acquires the resource file-system layer. Overridable so tests can
    /// substitute a layer wired to a temp folder without going through the
    /// workspace service hierarchy.
    /// </summary>
    protected virtual IFileStorage GetFileSystem()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        return workspaceWrapper.WorkspaceService.FileStorage;
    }

    /// <summary>
    /// Override this method to perform cleanup when the document is closed.
    /// Always call base.Cleanup() to ensure file-change monitoring is properly unregistered.
    /// </summary>
    public virtual void Cleanup()
    {
        _messengerService?.UnregisterAll(this);
    }

    /// <summary>
    /// Returns true when the on-disk size or mtime differs from the last-tracked
    /// save. The View's external-reload coalescer calls this between an
    /// in-flight reload and a queued follow-up to skip the follow-up when the
    /// disk content has not actually changed.
    /// </summary>
    public async Task<bool> IsFileChangedExternallyAsync()
    {
        // If we haven't saved yet, any change is considered external.
        if (_lastSavedFileMtime is null)
        {
            return true;
        }

        var fileStorage = GetFileSystem();
        var infoResult = await fileStorage.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return true;
        }

        return infoResult.Value.Size != _lastSavedFileSize
            || infoResult.Value.ModifiedUtc != _lastSavedFileMtime;
    }

    /// <summary>
    /// Reads the current disk size + mtime and caches them as the new tracking
    /// baseline. Called after every save and after every external reload so the
    /// next watcher event for the same content matches the cache and
    /// short-circuits. The body is effectively synchronous because GetInfoAsync
    /// is a single stat call with no real awaits; this matters so the UI thread
    /// cannot pump a watcher's ResourceChangedMessage between our write
    /// returning and the cache becoming current.
    /// </summary>
    public virtual async Task UpdateFileTrackingInfoAsync()
    {
        var fileStorage = GetFileSystem();
        var infoResult = await fileStorage.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            _lastSavedFileMtime = null;
            _lastSavedFileSize = 0;
            return;
        }

        _lastSavedFileSize = infoResult.Value.Size;
        _lastSavedFileMtime = infoResult.Value.ModifiedUtc;
    }
}
