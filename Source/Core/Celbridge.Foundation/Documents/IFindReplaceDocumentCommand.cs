using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Find and replace text within a document. Replacements are written directly
/// to disk. Any open document reloads its buffer from disk after the write.
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
    /// First line number (1-based, inclusive) to include in the replacement scope.
    /// Zero means no lower bound — all lines from the start of the file are included.
    /// </summary>
    int FromLine { get; set; }

    /// <summary>
    /// Last line number (1-based, inclusive) to include in the replacement scope.
    /// Zero means no upper bound — all lines to the end of the file are included.
    /// </summary>
    int ToLine { get; set; }
}
