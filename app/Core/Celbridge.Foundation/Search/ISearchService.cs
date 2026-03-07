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
}
