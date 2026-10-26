using System.Collections.Concurrent;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// One search by the Search panel. Starting a search cancels the run before it, and only the panel's current run
/// updates the results and the spinner. A run that shows results during the search keeps each reported file's
/// result. The search reports from a worker thread, and the panel takes the results on the UI thread.
/// </summary>
internal sealed class SearchRun : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentQueue<SearchFileResult> _foundResults = new();

    public SearchRun(bool showsResultsAsFound)
    {
        ShowsResultsAsFound = showsResultsAsFound;
        Token = _cancellation.Token;
    }

    /// <summary>
    /// True when the panel adds each file's result once the search finds it. Otherwise the search's results replace
    /// the panel's results when the search ends.
    /// </summary>
    public bool ShowsResultsAsFound { get; }

    public CancellationToken Token { get; }

    /// <summary>
    /// The callback that receives each matching file's result, or null when the run shows its results at the end.
    /// </summary>
    public Action<SearchFileResult>? FileResultFound => ShowsResultsAsFound ? _foundResults.Enqueue : null;

    /// <summary>
    /// Takes the results found since the last call, in path order.
    /// </summary>
    public List<SearchFileResult> TakeFoundResults()
    {
        var results = new List<SearchFileResult>();
        while (_foundResults.TryDequeue(out var result))
        {
            results.Add(result);
        }

        return results;
    }

    public void Cancel()
    {
        _cancellation.Cancel();
    }

    public void Dispose()
    {
        _cancellation.Dispose();
    }
}
