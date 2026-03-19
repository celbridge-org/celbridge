using Celbridge.Commands;

namespace Celbridge.Search;

/// <summary>
/// Opens the search panel and executes a search with the specified parameters.
/// Optionally enables replace mode with replacement text.
/// </summary>
public interface IShowSearchCommand : IExecutableCommand
{
    /// <summary>
    /// The text to search for.
    /// </summary>
    string SearchText { get; set; }

    /// <summary>
    /// Whether to match case when searching.
    /// </summary>
    bool MatchCase { get; set; }

    /// <summary>
    /// Whether to match whole words only.
    /// </summary>
    bool WholeWord { get; set; }

    /// <summary>
    /// Whether to enable replace mode in the search panel.
    /// </summary>
    bool ReplaceMode { get; set; }

    /// <summary>
    /// The replacement text (used when ReplaceMode is true).
    /// </summary>
    string ReplaceText { get; set; }
}
