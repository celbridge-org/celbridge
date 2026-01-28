namespace Celbridge.Search;

/// <summary>
/// Service interface for searching across project files.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Returns the Search Panel view.
    /// </summary>
    ISearchPanel? SearchPanel { get; }

    /// <summary>
    /// Searches for text across all project files.
    /// </summary>
    Task<SearchResults> SearchAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken);
}
