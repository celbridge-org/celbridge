using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with a document view.
/// </summary>
public interface IDocumentView : IWorkspaceItem
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
    /// Tells the view the size it will be laid out at once it is shown. A document in a background tab is
    /// never laid out, so a view that sizes its content before it is shown has nothing else to go on.
    /// Views that take their size from the layout alone ignore this.
    /// </summary>
    void SetExpectedLayoutSize(double width, double height);

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

    /// <summary>
    /// What the host knows about this document's hosted page still working.
    /// </summary>
    DocumentHealth GetHealth();

    /// <summary>
    /// True when the document can currently begin a find of its own (its content is ready). Drives the
    /// enabled state of the host's find affordance.
    /// </summary>
    bool CanFind { get; }

    /// <summary>
    /// Begins a find session, revealing and focusing the document's find affordance. Returns false when the
    /// document has no find of its own, or is not ready to start one.
    /// </summary>
    bool TryBeginFind();
}
