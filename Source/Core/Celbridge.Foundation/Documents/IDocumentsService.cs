using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// How an upcoming external reload should treat the editor's current view state.
/// View state is editor-specific, e.g. scroll position, selection, zoom level, and fold state.
/// </summary>
public enum ReloadHint
{
    /// <summary>
    /// Default. The editor keeps its current view state across the reload, regardless
    /// of any view state encoded in the on-disk file.
    /// </summary>
    PreserveViewState,

    /// <summary>
    /// The editor adopts the view state encoded in the on-disk file, or its default
    /// view state when the file format does not persist view state.
    /// </summary>
    DiskWinsOnViewState
}

/// <summary>
/// The editors offered by an "Open with..." dialog for a file: their ids, the display labels (with the
/// project default badged), and the index to preselect (the file's current effective editor, or the
/// default when that editor is no longer a candidate).
/// </summary>
public sealed record EditorPickList(
    IReadOnlyList<EditorId> EditorIds,
    IReadOnlyList<string> Labels,
    int SelectedIndex);

/// <summary>
/// A candidate editor for a file extension on the Project Settings File Types page: the editor id
/// written to the associations map, paired with its display name.
/// </summary>
public sealed record EditorCandidate(EditorId EditorId, string DisplayName);

/// <summary>
/// The editors that can open a given file extension and the one used by default (the first candidate),
/// for the Project Settings File Types page. Candidates are in resolution order; empty when nothing
/// claims the extension.
/// </summary>
public sealed record ExtensionEditorCandidates(
    IReadOnlyList<EditorCandidate> Candidates,
    EditorId DefaultEditorId);

/// <summary>
/// Options for opening a document in the documents panel.
/// </summary>
public record OpenDocumentOptions(
    DocumentAddress? Address = null,
    bool ForceReload = false,
    string Location = "",
    bool Activate = true,
    EditorId EditorId = default,
    string? EditorStateJson = null);

/// <summary>
/// Options for closing a document in the documents panel. ForceClose closes the document without letting
/// it cancel. SelectNeighbour decides whether another document takes over when the closing one was active.
/// </summary>
public record CloseDocumentOptions(
    bool ForceClose = false,
    bool SelectNeighbour = true);

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// The registry for document editor factories.
    /// </summary>
    IDocumentEditorRegistry DocumentEditorRegistry { get; }

    /// <summary>
    /// Restores the state of the documents panel from the previous session. A document that cannot be
    /// reopened is logged and skipped, so the restore always completes.
    /// </summary>
    Task RestorePanelState();

    /// <summary>
    /// The resource key for the currently active document.
    /// This is the empty resource if no document is currently active.
    /// </summary>
    ResourceKey ActiveDocument { get; }

    /// <summary>
    /// The document sections that are currently mounted, in reading order.
    /// This is a cached snapshot that is safe to read from any thread.
    /// </summary>
    IReadOnlyList<DocumentSection> VisibleSections { get; }

    /// <summary>
    /// The open documents as saveable workspace items. A utility docked into a document tab is not included.
    /// </summary>
    IReadOnlyList<ISaveableWorkspaceItem> GetSaveableItems();

    /// <summary>
    /// Returns a snapshot of all open documents with their addresses and editor IDs.
    /// This is a cached snapshot that is safe to read from any thread.
    /// </summary>
    IReadOnlyList<OpenDocumentInfo> GetOpenDocuments();

    /// <summary>
    /// The open document for a resource, or null when no document is open for it.
    /// </summary>
    OpenDocumentInfo? FindOpenDocument(ResourceKey fileResource);

    /// <summary>
    /// The document a section is currently showing, or empty when the section holds none. Each section
    /// keeps its own selection, so this is not the same as the active document.
    /// </summary>
    ResourceKey GetSelectedDocument(DocumentSection section);

    /// <summary>
    /// Creates a document view for the given file resource. When editorId is
    /// non-empty, uses that specific editor instead of the default resolution
    /// chain. Fails if the resource does not exist.
    /// </summary>
    Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource, EditorId editorId = default);

    /// <summary>
    /// Returns the document view type for the specified file resource.
    /// </summary>
    DocumentViewType GetDocumentViewType(ResourceKey fileResource);

    /// <summary>
    /// Returns the active document's view as a findable document when it owns a host find bar, otherwise null
    /// (including when no workspace is loaded).
    /// </summary>
    IFindableDocument? GetActiveFindableDocument();

    /// <summary>
    /// Determines if a file resource can be opened as a document in the editor.
    /// Returns false if the file format is not supported or the resource is not a file.
    /// </summary>
    bool IsDocumentSupported(ResourceKey fileResource);

    /// <summary>
    /// Returns the text editor language associated with the specified file resource.
    /// Returns an empty string if no matching language is found.
    /// </summary>
    string GetDocumentLanguage(ResourceKey fileResource);

    /// <summary>
    /// Returns the file's per-file editor override from the sidecar's '_editor' field, or
    /// EditorId.Empty when the file has none.
    /// </summary>
    Task<EditorId> GetPreferredEditorAsync(ResourceKey fileResource);

    /// <summary>
    /// Records the user's editor choice for a file. Writes the sidecar '_editor' override when the
    /// choice differs from the project default, and clears it when the choice is the default, so the
    /// sidecar only ever stores a deviation.
    /// </summary>
    Task<Result> SetPreferredEditorAsync(ResourceKey fileResource, EditorId editorId);

    /// <summary>
    /// Builds the "Open with..." choices for a file: the pickable editors, their badged labels, and the
    /// index to preselect. Returns null when fewer than two editors can open the file, so no choice is
    /// worth offering.
    /// </summary>
    EditorPickList? GetEditorPickList(ResourceKey fileResource, EditorId currentEditorId);

    /// <summary>
    /// The editors that can open the given file extension, and the default among them, for the Project
    /// Settings File Types page. Mirrors the runtime resolution used to open a file of that extension.
    /// </summary>
    ExtensionEditorCandidates GetEditorCandidatesForExtension(string fileExtension);

    /// <summary>
    /// True when a registered editor reserves the extension for a role the application depends on. The
    /// File Types page leaves these out, because pointing one at a different editor breaks the
    /// application rather than customizing it.
    /// </summary>
    bool IsReservedFileType(string fileExtension);

    /// <summary>
    /// Opens a file resource as a document in the documents panel.
    /// </summary>
    Task<Result<OpenDocumentOutcome>> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null);

    /// <summary>
    /// Closes an opened document in the documents panel.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource, CloseDocumentOptions? options = null);

    /// <summary>
    /// Activates an opened document in the documents panel, making it the active tab.
    /// Fails if the specified document is not opened.
    /// </summary>
    Result ActivateDocument(ResourceKey fileResource);

    /// <summary>
    /// Stores the open documents and their addresses in persistent storage. These documents are
    /// reopened at the start of the next editing session. Persistence is best effort: a failure is
    /// logged rather than reported.
    /// </summary>
    Task StoreOpenDocumentAddresses();

    /// <summary>
    /// Stores the currently active document in persistent storage.
    /// This document will be activated at the start of the next editing session. Persistence is best
    /// effort: a failure is logged rather than reported.
    /// </summary>
    Task StoreActiveDocument();

    /// <summary>
    /// Saves editor state (scroll position, view mode, etc.) for all open documents. Persistence is
    /// best effort: a failure is logged rather than reported.
    /// </summary>
    Task StoreDocumentEditorStates();

    /// <summary>
    /// Saves editor state for a single document. Pass a non-empty state string to persist,
    /// or null/empty to clear any existing entry for the resource. Persistence is best effort: a
    /// failure is logged rather than reported.
    /// </summary>
    Task StoreDocumentEditorState(ResourceKey fileResource, string? state);

    /// <summary>
    /// Records a hint that the next watcher-driven reload of the resource should honour,
    /// overwriting any prior hint for the same resource. Hints expire if not consumed
    /// within a short window.
    /// </summary>
    void RegisterReloadHint(ResourceKey fileResource, ReloadHint hint);

    /// <summary>
    /// Returns the most recently registered hint for the resource and removes it
    /// from the store. Returns PreserveViewState when no fresh hint is set.
    /// </summary>
    ReloadHint ConsumeReloadHint(ResourceKey fileResource);
}
