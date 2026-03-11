namespace Celbridge.Search;

/// <summary>
/// Service interface for searching and replacing text across project files.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches for text across all project files.
    /// When maxResults is specified, search stops after finding that many matches (for display purposes).
    /// When maxResults is null, all matches are returned (for Replace All operations).
    /// </summary>
    Task<SearchResults> SearchAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        int? maxResults,
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

    /// <summary>
    /// Gets the search and replace history for the current workspace.
    /// </summary>
    Task<SearchHistory> GetHistoryAsync();

    /// <summary>
    /// Adds a search term to the history.
    /// </summary>
    Task AddSearchTermToHistoryAsync(string term);

    /// <summary>
    /// Adds a replace term to the history.
    /// </summary>
    Task AddReplaceTermToHistoryAsync(string term);

    /// <summary>
    /// Clears all search and replace history.
    /// </summary>
    Task ClearHistoryAsync();
}
