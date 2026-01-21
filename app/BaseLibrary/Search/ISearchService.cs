namespace Celbridge.Explorer;

/// <summary>
/// Service interface for searching across project files.
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
}
