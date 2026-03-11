using Celbridge.Logging;
using Celbridge.Workspace;
using Path = System.IO.Path;

namespace Celbridge.Search.Services;

/// <summary>
/// Service that performs text search across project files.
/// </summary>
public class SearchService : ISearchService, IDisposable
{
    private readonly ILogger<SearchService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly FileFilter _fileFilter;
    private readonly TextMatcher _textMatcher;
    private readonly SearchResultFormatter _formatter;
    private readonly TextReplacer _textReplacer;

    public SearchService(
        ILogger<SearchService> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileFilter = new FileFilter();
        _textMatcher = new TextMatcher();
        _formatter = new SearchResultFormatter();
        _textReplacer = new TextReplacer();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
            }

            _disposed = true;
        }
    }

    ~SearchService()
    {
        Dispose(false);
    }

    private sealed record SearchState
    {
        public int TotalMatches { get; set; }
        public bool ReachedMaxResults { get; set; }
    }

    public Task<SearchResults> SearchAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        int? maxResults,
        CancellationToken cancellationToken)
    {
        return SearchInternalAsync(searchTerm, matchCase, wholeWord, maxResults, cancellationToken);
    }

    private async Task<SearchResults> SearchInternalAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        int? maxResults,
        CancellationToken cancellationToken)
    {
        var fileResults = new List<SearchFileResult>();

        if (string.IsNullOrEmpty(searchTerm))
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false);
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false);
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var projectFolder = resourceRegistry.ProjectFolderPath;

        if (string.IsNullOrEmpty(projectFolder) ||
            !Directory.Exists(projectFolder))
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false);
        }

        var searchState = new SearchState();

        try
        {
            // Get all file resources from the registry (already sorted by path)
            var fileResources = resourceRegistry.GetAllFileResources();

            await Task.Run(() =>
            {
                foreach (var (resource, filePath) in fileResources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (maxResults.HasValue && searchState.TotalMatches >= maxResults.Value)
                    {
                        searchState.ReachedMaxResults = true;
                        break;
                    }

                    var remainingMatches = maxResults.HasValue
                        ? maxResults.Value - searchState.TotalMatches
                        : int.MaxValue;

                    var fileResult = SearchFile(
                        filePath,
                        projectFolder,
                        resource,
                        searchTerm,
                        matchCase,
                        wholeWord,
                        remainingMatches,
                        cancellationToken);

                    if (fileResult != null && fileResult.Matches.Count > 0)
                    {
                        fileResults.Add(fileResult);
                        searchState.TotalMatches += fileResult.Matches.Count;
                    }
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new SearchResults(searchTerm, fileResults, searchState.TotalMatches, fileResults.Count, true, searchState.ReachedMaxResults);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during search: {ex.Message}");
        }

        return new SearchResults(searchTerm, fileResults, searchState.TotalMatches, fileResults.Count, false, searchState.ReachedMaxResults);
    }

    private SearchFileResult? SearchFile(
        string filePath,
        string rootDirectory,
        ResourceKey resourceKey,
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        int maxMatches,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if file should be searched (size, extension filters)
            if (!_fileFilter.ShouldSearchFile(filePath))
            {
                return null;
            }

            // Check if file content is text (not binary) using efficient sampling
            if (!_fileFilter.IsTextFile(filePath))
            {
                return null;
            }

            // Try to read the file content
            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch
            {
                return null;
            }

            var matches = new List<SearchMatchLine>();
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length && matches.Count < maxMatches; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = lines[i].TrimEnd('\r');
                var lineMatches = _textMatcher.FindMatches(line, searchTerm, matchCase, wholeWord);

                foreach (var match in lineMatches)
                {
                    if (matches.Count >= maxMatches)
                        break;

                    var (contextLine, displayMatchStart) = _formatter.FormatContextLine(line, match.Start, match.Length);
                    matches.Add(new SearchMatchLine(
                        i + 1, // Line numbers are 1-based
                        contextLine,
                        displayMatchStart,
                        match.Length,
                        match.Start)); // Store original position for navigation
                }
            }

            if (matches.Count == 0)
            {
                return null;
            }

            var relativePath = GetRelativePath(filePath, rootDirectory);
            var fileName = Path.GetFileName(filePath);

            return new SearchFileResult(resourceKey, fileName, relativePath, matches);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string GetRelativePath(string filePath, string rootDirectory)
    {
        if (filePath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var relative = filePath.Substring(rootDirectory.Length);
            return relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return filePath;
    }

    public async Task<ReplaceResult> ReplaceInFileAsync(
        ResourceKey resource,
        string searchText,
        string replaceText,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return new ReplaceResult(false, 0);
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return new ReplaceResult(false, 0);
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        string filePath;
        try
        {
            filePath = resourceRegistry.GetResourcePath(resource);
        }
        catch (ArgumentException)
        {
            return new ReplaceResult(false, 0);
        }

        try
        {
            return await Task.Run(() => ReplaceInFile(
                filePath,
                searchText,
                replaceText,
                matchCase,
                wholeWord,
                cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ReplaceResult(false, 0);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, $"IO error replacing in file: {filePath}");
            return new ReplaceResult(false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error replacing in file: {filePath}");
            return new ReplaceResult(false, 0);
        }
    }

    private ReplaceResult ReplaceInFile(
        string filePath,
        string searchText,
        string replaceText,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (IOException)
        {
            return new ReplaceResult(false, 0);
        }

        var (newContent, totalReplacements) = _textReplacer.ReplaceAll(
            content,
            searchText,
            replaceText,
            matchCase,
            wholeWord);

        if (totalReplacements == 0)
        {
            return new ReplaceResult(true, 0);
        }

        try
        {
            File.WriteAllText(filePath, newContent);
        }
        catch (IOException)
        {
            return new ReplaceResult(false, 0);
        }

        return new ReplaceResult(true, totalReplacements);
    }

    public async Task<ReplaceMatchResult> ReplaceMatchAsync(
        ResourceKey resource,
        string searchText,
        string replaceText,
        int lineNumber,
        int originalMatchStart,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return new ReplaceMatchResult(false);
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return new ReplaceMatchResult(false);
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        string filePath;
        try
        {
            filePath = resourceRegistry.GetResourcePath(resource);
        }
        catch (ArgumentException)
        {
            return new ReplaceMatchResult(false);
        }

        try
        {
            return await Task.Run(() => ReplaceMatch(
                filePath,
                searchText,
                replaceText,
                lineNumber,
                originalMatchStart,
                matchCase,
                wholeWord,
                cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ReplaceMatchResult(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, $"IO error replacing match in file: {filePath}");
            return new ReplaceMatchResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error replacing match in file: {filePath}");
            return new ReplaceMatchResult(false);
        }
    }

    private ReplaceMatchResult ReplaceMatch(
        string filePath,
        string searchText,
        string replaceText,
        int lineNumber,
        int originalMatchStart,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (IOException)
        {
            return new ReplaceMatchResult(false);
        }

        var (newContent, success) = _textReplacer.ReplaceMatch(
            content,
            searchText,
            replaceText,
            lineNumber,
            originalMatchStart,
            matchCase,
            wholeWord);

        if (!success)
        {
            return new ReplaceMatchResult(false);
        }

        try
        {
            File.WriteAllText(filePath, newContent);
        }
        catch (IOException)
        {
            return new ReplaceMatchResult(false);
        }

        return new ReplaceMatchResult(true);
    }

    public async Task<ReplaceAllResult> ReplaceAllAsync(
        List<SearchFileResult> fileResults,
        string searchText,
        string replaceText,
        bool matchCase,
        bool wholeWord,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return new ReplaceAllResult(0, 0, 0, false);
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return new ReplaceAllResult(0, 0, 0, false);
        }

        int totalReplacements = 0;
        int filesModified = 0;
        int filesFailed = 0;

        try
        {
            foreach (var fileResult in fileResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ReplaceInFileAsync(
                    fileResult.Resource,
                    searchText,
                    replaceText,
                    matchCase,
                    wholeWord,
                    cancellationToken);

                if (result.Success && result.ReplacementsCount > 0)
                {
                    totalReplacements += result.ReplacementsCount;
                    filesModified++;
                }
                else if (!result.Success)
                {
                    filesFailed++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new ReplaceAllResult(totalReplacements, filesModified, filesFailed, true);
        }

        return new ReplaceAllResult(totalReplacements, filesModified, filesFailed, false);
    }

    private const string SearchHistoryKey = "SearchHistory";
    private const int MaxHistoryItems = 15;

    public async Task<SearchHistory> GetHistoryAsync()
    {
        var emptySearchTerms = new List<string>();
        var emptyReplaceTerms = new List<string>();
        var emptyHistory = new SearchHistory(emptySearchTerms, emptyReplaceTerms);

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return emptyHistory;
        }

        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;

        try
        {
            var history = await workspaceSettings.GetPropertyAsync<SearchHistory>(SearchHistoryKey);

            if (history is null)
            {
                return emptyHistory;
            }

            return history;
        }
        catch (Exception ex)
        {
            // Handle corrupted or old-format data by clearing it and returning empty history
            _logger.LogWarning(ex, "Failed to deserialize search history, clearing corrupted data");
            await workspaceSettings.DeletePropertyAsync(SearchHistoryKey);
            return emptyHistory;
        }
    }

    public async Task AddSearchTermToHistoryAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        var history = await GetHistoryAsync();
        var searchTerms = history.SearchTerms.ToList();

        // Remove any existing entry with the same term (case-sensitive)
        searchTerms.Remove(term);

        // Add new entry at the beginning
        searchTerms.Insert(0, term);

        // Trim to max size
        if (searchTerms.Count > MaxHistoryItems)
        {
            searchTerms = searchTerms.Take(MaxHistoryItems).ToList();
        }

        var updatedHistory = new SearchHistory(searchTerms, history.ReplaceTerms.ToList());
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        await workspaceSettings.SetPropertyAsync(SearchHistoryKey, updatedHistory);
    }

    public async Task AddReplaceTermToHistoryAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        var history = await GetHistoryAsync();
        var replaceTerms = history.ReplaceTerms.ToList();

        // Remove any existing entry with the same term (case-sensitive)
        replaceTerms.Remove(term);

        // Add new entry at the beginning
        replaceTerms.Insert(0, term);

        // Trim to max size
        if (replaceTerms.Count > MaxHistoryItems)
        {
            replaceTerms = replaceTerms.Take(MaxHistoryItems).ToList();
        }

        var updatedHistory = new SearchHistory(history.SearchTerms.ToList(), replaceTerms);
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        await workspaceSettings.SetPropertyAsync(SearchHistoryKey, updatedHistory);
    }

    public async Task ClearHistoryAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        await workspaceSettings.DeletePropertyAsync(SearchHistoryKey);
    }
}
