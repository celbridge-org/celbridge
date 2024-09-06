using Celbridge.Explorer;

namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with a document view.
/// </summary>
public interface IDocumentView
{
    /// <summary>
    /// Sets the file resource for the document.
    /// Fails if the file resource does not exist on disk.
    /// </summary>
    Result SetFileResource(ResourceKey fileResource);

    /// <summary>
    /// Load the content of the document from the previously set file path.
    /// </summary>
    Task<Result> LoadContent();

    /// <summary>
    /// Flag that indicates if the document has been modified and requires saving.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Update the save timer on the document to avoid writing to disk too frequently.
    /// Returns true when the timer has expired, and the file should now be saved.
    /// Fails if the document does not have unsaved changes.
    /// </summary>
    Result<bool> UpdateSaveTimer(double deltaTime);

    /// <summary>
    /// Save the document to disk.
    /// </summary>
    Task<Result> SaveDocument();

    /// <summary>
    /// Returns true if the document can be closed.
    /// For example, a document view could prompt the user to confirm closing the document, and return false
    /// here to indicate that the user cancelled the close operation.
    /// </summary>
    Task<bool> CanCloseDocument();

    /// <summary>
    /// Called when the document is about to close.
    /// Allows the document view to clear its state before it closes.
    /// </summary>
    void OnDocumentClosing();
}
