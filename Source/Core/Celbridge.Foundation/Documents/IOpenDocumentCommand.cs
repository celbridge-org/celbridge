using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// A document to open and where to land in it, recorded so a producer can offer the navigation
/// without issuing the command itself. Line and column are one-based; zero opens at the top. Label
/// names the action for the surface that offers it, and is resolved by whoever recorded it.
/// </summary>
public record OpenDocumentAction(
    ResourceKey Resource,
    string? Label = null,
    int Line = 0,
    int Column = 0);

/// <summary>
/// Open a document in the documents panel.
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
    /// Optional target section to open the document in.
    /// If null, a new document opens in Main's primary section and an already open document stays where it is.
    /// </summary>
    DocumentSection? TargetSection { get; set; }

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
    EditorId EditorId { get; set; }

    /// <summary>
    /// When set, restores this editor state after the document opens.
    /// </summary>
    string? EditorStateJson { get; set; }
}
