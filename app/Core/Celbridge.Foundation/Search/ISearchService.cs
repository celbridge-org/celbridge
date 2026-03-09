namespace Celbridge.Search;

/// <summary>
/// Service interface for searching and replacing text across project files.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches for text across all project files.
    /// </summary>
    Task<SearchResults> SearchAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces all occurrences of search text with replacement text in a single file.
    /// </summary>
    Task<ReplaceResult> ReplaceInFileAsync(
        ResourceKey resource,
        string searchText,
        string replaceText,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a single match occurrence at a specific line and column in a file.
    /// </summary>
    Task<ReplaceMatchResult> ReplaceMatchAsync(
        ResourceKey resource,
        string searchText,
        string replaceText,
        int lineNumber,
        int originalMatchStart,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces all occurrences of search text across all provided file results.
    /// </summary>
    Task<ReplaceAllResult> ReplaceAllAsync(
        List<SearchFileResult> fileResults,
        string searchText,
        string replaceText,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken);
}
