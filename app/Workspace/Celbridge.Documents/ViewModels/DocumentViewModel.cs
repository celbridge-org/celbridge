using CommunityToolkit.Mvvm.ComponentModel;
using System.Security.Cryptography;

namespace Celbridge.Documents.ViewModels;

public abstract partial class DocumentViewModel : ObservableObject
{
    [ObservableProperty]
    private ResourceKey _fileResource = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges = false;

    // Track the hash and size of the last saved file to detect genuine external changes
    private string? _lastSavedFileHash;
    private long _lastSavedFileSize;

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
