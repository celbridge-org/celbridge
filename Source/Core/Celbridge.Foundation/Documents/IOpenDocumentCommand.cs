using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Open a document in the documents panel. ResultValue reports whether the open completed
/// or was cancelled, so automation consumers (such as MCP tools and scripts) can distinguish
/// a no-op close refusal from a genuine open.
/// </summary>
public interface IOpenDocumentCommand : IExecutableCommand<OpenDocumentOutcome>
{
    /// <summary>
    /// The resource key of the file to open.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Reload the document from the file, if the document is already open.
    /// </summary>
    bool ForceReload { get; set; }

    /// <summary>
    /// Optional location within the document to navigate to when opening.
    /// </summary>
    string Location { get; set; }

    /// <summary>
    /// Optional target section index (0, 1, or 2) to open the document in.
    /// If null, the document opens in the active section.
    /// </summary>
    int? TargetSectionIndex { get; set; }

    /// <summary>
    /// Optional tab position within the section.
    /// If null, the tab is appended at the end.
    /// </summary>
    int? TargetTabIndex { get; set; }

    /// <summary>
    /// When true, the opened document becomes the active tab.
    /// When false, the document is opened but the currently active tab is preserved.
    /// </summary>
    bool Activate { get; set; }

    /// <summary>
    /// When set, opens the document with this specific editor instead of the default.
    /// </summary>
    DocumentEditorId EditorId { get; set; }

    /// <summary>
    /// When set, restores this editor state after the document opens.
    /// </summary>
    string? EditorStateJson { get; set; }
}
