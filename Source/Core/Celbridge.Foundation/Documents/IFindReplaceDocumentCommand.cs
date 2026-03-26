using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Find and replace text within a document.
/// When OpenDocument is true (default) or the document is already open, replacements are applied
/// through the editor with full undo support. When OpenDocument is false and the document is
/// not open, replacements are applied directly to the file on disk.
/// </summary>
public interface IFindReplaceDocumentCommand : IExecutableCommand<int>
{
    /// <summary>
    /// The resource key of the file to perform find and replace on.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The text to search for.
    /// </summary>
    string SearchText { get; set; }

    /// <summary>
    /// The replacement text.
    /// </summary>
    string ReplaceText { get; set; }

    /// <summary>
    /// If true, the search is case-sensitive.
    /// </summary>
    bool MatchCase { get; set; }

    /// <summary>
    /// If true, the search text is treated as a regular expression.
    /// </summary>
    bool UseRegex { get; set; }

    /// <summary>
    /// When true (default), opens the document in the editor and applies replacements with undo support.
    /// When false and the document is not already open, applies replacements directly to the file on disk.
    /// When false but the document is already open, routes through the editor to avoid auto-save race conditions.
    /// </summary>
    bool OpenDocument { get; set; }
}
