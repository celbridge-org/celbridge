using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with a document view.
/// </summary>
public interface IDocumentView : ISaveableWorkspaceItem
{
    /// <summary>
    /// Id of the factory that produced this view. Immutable for the view's lifetime.
    /// </summary>
    EditorId EditorId { get; }

    /// <summary>
    /// Sets the file resource for the document view. FileResource is empty until this is called.
    /// Fails if the resource does not exist in the resource registry or in the file system.
    /// </summary>
    Task<Result> SetFileResource(ResourceKey fileResource);

    /// <summary>
    /// Load the document content into the document view using the previously set file resource.
    /// </summary>
    Task<Result> LoadContent();

    /// <summary>
    /// Sets the document's writable state.
    /// </summary>
    void SetWritableState(WritableState state);

    /// <summary>
    /// Navigate to a specific location within the document.
    /// </summary>
    Task<Result> NavigateToLocation(string location);

    /// <summary>
    /// What Edit commands act on while this document has focus.
    /// </summary>
    IEditTarget EditTarget { get; }

    /// <summary>
    /// Gives this document keyboard focus and reports the focus change so any previously focused surface
    /// is released. A view whose surface is still initializing takes focus as soon as it is ready. Views
    /// with no focusable surface do nothing.
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
