namespace Celbridge.Search;

/// <summary>
/// Service interface for searching text across project files.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches the text of the files in scope for the pattern in the options. The search reads the files in path
    /// order, and calls fileResultFound with each matching file's result once the search has read the file. The
    /// calls come from a worker thread, in the order of the results returned.
    /// </summary>
    Task<SearchResults> SearchAsync(
        SearchOptions options,
        Action<SearchFileResult>? fileResultFound,
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
