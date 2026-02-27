using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Security.Cryptography;

namespace Celbridge.Documents.ViewModels;

public abstract partial class DocumentViewModel : ObservableObject
{
    // Delay before saving the document after the most recent change
    protected const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private ResourceKey _fileResource = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges = false;

    [ObservableProperty]
    private double _saveTimer;

    // Flag to suppress reload requests triggered by our own save operations.
    // This prevents the file watcher race condition where the watcher fires
    // before we can update our tracking info.
    protected bool IsSavingFile { get; set; }

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
    /// Override this method to perform cleanup when the document is closed.
    /// </summary>
    public virtual void Cleanup()
    {
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

    protected void UpdateFileTrackingInfo()
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
}
