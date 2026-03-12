using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;

namespace Celbridge.Documents.Extensions;

/// <summary>
/// View model for extension document editors.
/// Provides text file I/O and file-change monitoring for custom extension editors.
/// </summary>
public partial class ExtensionDocumentViewModel : DocumentViewModel
{
    public ExtensionDocumentViewModel(IMessengerService messengerService)
    {
        EnableFileChangeMonitoring(messengerService);
    }

    /// <summary>
    /// Loads text content from the file. Returns empty string if the file doesn't exist.
    /// </summary>
    public async Task<string> LoadTextContentAsync()
    {
        if (!File.Exists(FilePath))
        {
            return string.Empty;
        }

        var content = await File.ReadAllTextAsync(FilePath);
        UpdateFileTrackingInfo();
        return content;
    }

    /// <summary>
    /// Saves text content to the file.
    /// </summary>
    public async Task<Result> SaveTextContentAsync(string content)
    {
        return await SaveTextToFileAsync(content);
    }

    /// <summary>
    /// Called after a successful save to reset the save state flags.
    /// </summary>
    public void OnSaveCompleted()
    {
        HasUnsavedChanges = false;
        SaveTimer = 0;
    }

    /// <summary>
    /// Updates the file tracking information so that file-change monitoring
    /// can detect external changes. Call this after the initial content load.
    /// </summary>
    public void InitializeFileTracking()
    {
        UpdateFileTrackingInfo();
    }
}
