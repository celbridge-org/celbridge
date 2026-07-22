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
    /// Id of the factory that produced this view. Immutable for the view's lifetime.
    /// </summary>
    EditorId EditorId { get; }

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
    /// Fails if HasUnsavedChanges is false.
    /// </summary>
    Result<bool> UpdateSaveTimer(double deltaTime);

    /// <summary>
    /// Save the document content from the document view using the previously set file resource.
    /// </summary>
    Task<Result> SaveDocument();

    /// <summary>
    /// The document's writable state, or the reason it is non-editable.
    /// </summary>
    WritableState WritableState { get; }

    /// <summary>
    /// Sets the document's writable state.
    /// </summary>
    void SetWritableState(WritableState state);

    /// <summary>
    /// Navigate to a specific location within the document.
    /// </summary>
    Task<Result> NavigateToLocation(string location);

    /// <summary>
    /// Gives this document keyboard focus and reports the focus change so any previously focused surface
    /// is released. Views with no focusable surface do nothing.
    /// </summary>
    void FocusDocument();

    /// <summary>
    /// Returns true if the document view can be closed. Returning false cancels the close operation.
    /// </summary>
    Task<bool> CanClose();

    /// <summary>
    /// Called when the document is about to close. Use this to clear the document view state
    /// and free resources.
    /// </summary>
    Task PrepareToClose();

    /// <summary>
    /// Captures the editor's UI state as an opaque JSON string, or null if no state is available.
    /// </summary>
    Task<string?> TrySaveEditorStateAsync();

    /// <summary>
    /// Restores previously saved editor state from an opaque JSON string.
    /// </summary>
    Task RestoreEditorStateAsync(string state);
}
