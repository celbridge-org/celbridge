namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with a document view.
/// </summary>
public interface IDocumentView
{
    /// <summary>
    /// Sets the file resource for the document view.
    /// Fails if the resource does not exist in the resource registry or in the file system.
    /// </summary>
    Task<Result> SetFileResource(ResourceKey fileResource);

    /// <summary>
    /// Load the document content into the document view using the previously set file resource.
    /// </summary>
    Task<Result> LoadContent();

    /// <summary>
    /// Flag that indicates if the document view has been modified and requires saving.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// The document view may use a save timer to avoid writing to disk too frequently.
    /// Returns true when the timer has expired, and the file should now be saved.
    /// Fails if the HasUnsavedChanges is false.
    /// </summary>
    Result<bool> UpdateSaveTimer(double deltaTime);

    /// <summary>
    /// Save the document content from the document view using the previously set file resource.
    /// </summary>
    Task<Result> SaveDocument();

    /// <summary>
    /// Returns true if the document view can be closed.
    /// For example, a document view could prompt the user to confirm closing the document, and return false
    /// here to indicate that the user cancelled the close operation. 
    /// </summary>
    Task<bool> CanClose();

    /// <summary>
    /// Called when the document is about to close. 
    /// This can be used to clear the document view state and free resources, etc. before the document view closes. 
    /// This approach is used instead of the Dispose Pattern to support pooling use cases.
    /// </summary>
    void PrepareToClose();
}
