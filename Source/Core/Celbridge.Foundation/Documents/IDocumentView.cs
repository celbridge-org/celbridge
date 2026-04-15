namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with a document view.
/// </summary>
public interface IDocumentView
{
    /// <summary>
    /// Gets the file resource key for this document.
    /// Returns an empty ResourceKey if SetFileResource has not been called.
    /// </summary>
    ResourceKey FileResource { get; }

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
    /// Navigate to a specific location within the document.
    /// </summary>
    Task<Result> NavigateToLocation(string location);

    /// <summary>
    /// Applies a batch of text edits to the document as a single undo unit.
    /// Each edit specifies a range (line, column, endLine, endColumn) and replacement text.
    /// Returns failure if the document view does not support text editing.
    /// </summary>
    Task<Result> ApplyEditsAsync(IEnumerable<TextEdit> edits);

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
    Task PrepareToClose();

    /// <summary>
    /// Whether the editor is ready to save and restore state.
    /// Returns false while the editor is still initializing (e.g., WebView loading).
    /// StoreEditorStates skips documents that are not ready, preserving their previously saved state.
    /// </summary>
    bool IsEditorStateReady { get; }

    /// <summary>
    /// Saves the editor's UI state as an opaque JSON string.
    /// Returns null if the editor does not support state preservation.
    /// Only called when IsEditorStateReady is true.
    /// </summary>
    Task<string?> SaveEditorStateAsync();

    /// <summary>
    /// Restores previously saved editor state from an opaque JSON string.
    /// </summary>
    Task RestoreEditorStateAsync(string state);
}
