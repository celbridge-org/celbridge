using System.Text.RegularExpressions;
using Celbridge.Logging;
using Celbridge.Resources;
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
    private bool _disposed;

    private IFileStorage FileStorage => _workspaceWrapper.WorkspaceService.FileStorage;
    private IResourceRegistry ResourceRegistry => _workspaceWrapper.WorkspaceService.ResourceService.Registry;
    private IWorkspaceSettings WorkspaceSettings => _workspaceWrapper.WorkspaceService.WorkspaceSettings;

    public SearchService(
        ILogger<SearchService> logger,
        IWorkspaceWrapper workspaceWrapper,
        ITextBinarySniffer textBinarySniffer)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileFilter = new FileFilter(textBinarySniffer);
        _textMatcher = new TextMatcher();
        _formatter = new SearchResultFormatter();
        _textReplacer = new TextReplacer();
    }

    private sealed record SearchState
    {
        public int TotalMatches { get; set; }
        public bool ReachedMaxResults { get; set; }
    }

    public async Task<SearchResults> SearchAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        int? maxResults,
        CancellationToken cancellationToken,
        bool useRegex = false,
        string include = "",
        string exclude = "",
        string scope = "")
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

        var projectFolder = ResourceRegistry.ProjectFolderPath;

        if (string.IsNullOrEmpty(projectFolder))
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false);
        }

        Regex? searchRegex = null;
        if (useRegex)
        {
            try
            {
                var regexOptions = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                searchRegex = new Regex(searchTerm, regexOptions);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning($"Invalid regex pattern '{searchTerm}': {ex.Message}");
                return new SearchResults(searchTerm, fileResults, 0, 0, false, false);
            }
        }

        Regex? includeRegex = null;
        if (!string.IsNullOrEmpty(include))
        {
            var patterns = include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var regexPatterns = patterns.Select(GlobPatternToRegex);
            var combined = string.Join("|", regexPatterns.Select(pattern => $"(?:{pattern})"));
            includeRegex = new Regex(combined, RegexOptions.IgnoreCase);
        }

        var searchState = new SearchState();

        try
        {
            // Get all file resources from the registry (already sorted by path)
            var fileResources = ResourceRegistry.GetAllFileResources();

            if (includeRegex != null)
            {
                fileResources = fileResources
                    .Where(entry => includeRegex.IsMatch(Path.GetFileName(entry.Path)))
                    .ToList();
            }

            if (!string.IsNullOrEmpty(exclude))
            {
                var excludePatterns = exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var excludeRegexPatterns = excludePatterns.Select(GlobPatternToRegex);
                var excludeCombined = string.Join("|", excludeRegexPatterns.Select(pattern => $"(?:{pattern})"));
                var excludeRegex = new Regex(excludeCombined, RegexOptions.IgnoreCase);
                fileResources = fileResources
                    .Where(entry => !excludeRegex.IsMatch(Path.GetFileName(entry.Path)))
                    .ToList();
            }

            if (!string.IsNullOrEmpty(scope))
            {
                // Canonicalize the scope so callers can pass a bare path
                // ("Data") or the fully-prefixed form ("project:Data")
                // interchangeably, matching the convention used by every other
                // resource-addressing tool. The comparison runs against the
                // canonical "<root>:<path>" form of each candidate, so a bare
                // scope without a root prefix never matched otherwise.
                string canonicalScope;
                if (ResourceKey.TryCreate(scope, out var scopeKey))
                {
                    canonicalScope = scopeKey.ToString();
                }
                else
                {
                    canonicalScope = scope;
                }

                fileResources = fileResources
                    .Where(entry => entry.Resource.ToString().StartsWith(canonicalScope, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            cancellationToken.ThrowIfCancellationRequested();
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

                var fileResult = await SearchFileAsync(
                    filePath,
                    projectFolder,
                    resource,
                    searchTerm,
                    matchCase,
                    wholeWord,
                    remainingMatches,
                    cancellationToken,
                    searchRegex);

                if (fileResult != null && fileResult.Matches.Count > 0)
                {
                    fileResults.Add(fileResult);
                    searchState.TotalMatches += fileResult.Matches.Count;
                }
            }
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

    private async Task<SearchFileResult?> SearchFileAsync(
        string filePath,
        string rootDirectory,
        ResourceKey resourceKey,
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        int maxMatches,
        CancellationToken cancellationToken,
        Regex? searchRegex = null)
    {
        try
        {
            // Check if file should be searched (size, extension filters)
            if (!await _fileFilter.ShouldSearchFileAsync(FileStorage, resourceKey, filePath))
            {
                return null;
            }

            // Check if file content is text (not binary) using efficient sampling
            if (!_fileFilter.IsTextFile(filePath))
            {
                return null;
            }

            // Stream the file via the chokepoint so reads pick up the same
            // containment validation as writes and large files do not load
            // fully into memory.
            var openResult = await FileStorage.OpenReadAsync(resourceKey);
            if (openResult.IsFailure)
            {
                return null;
            }

            var matches = new List<SearchMatchLine>();
            await using (var stream = openResult.Value)
            using (var reader = new StreamReader(stream))
            {
                int lineNumber = 0;
                string? rawLine;
                while ((rawLine = await reader.ReadLineAsync(cancellationToken)) is not null
                    && matches.Count < maxMatches)
                {
                    lineNumber++;
                    var line = rawLine.TrimEnd('\r');

                    var lineMatches = searchRegex != null
                        ? _textMatcher.FindRegexMatches(line, searchRegex)
                        : _textMatcher.FindMatches(line, searchTerm, matchCase, wholeWord);

                    foreach (var match in lineMatches)
                    {
                        if (matches.Count >= maxMatches)
                            break;

                        var (contextLine, displayMatchStart) = _formatter.FormatContextLine(line, match.Start, match.Length);
                        matches.Add(new SearchMatchLine(
                            lineNumber,
                            contextLine,
                            displayMatchStart,
                            match.Length,
                            match.Start));
                    }
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
        catch (OperationCanceledException)
        {
            // Let cancellation propagate so the outer loop returns a Cancelled
            // result rather than treating the file as unsearchable.
            throw;
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

        var resolveReplaceResult = ResourceRegistry.ResolveResourcePath(resource);
        if (resolveReplaceResult.IsFailure)
        {
            return new ReplaceResult(false, 0);
        }
        var filePath = resolveReplaceResult.Value;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readResult = await FileStorage.ReadAllTextAsync(resource);
            if (readResult.IsFailure)
            {
                return new ReplaceResult(false, 0);
            }
            var content = readResult.Value;

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

            var writeResult = await FileStorage.WriteAllTextAsync(resource, newContent);
            if (writeResult.IsFailure)
            {
                return new ReplaceResult(false, 0);
            }

            return new ReplaceResult(true, totalReplacements);
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

        var resolveMatchResult = ResourceRegistry.ResolveResourcePath(resource);
        if (resolveMatchResult.IsFailure)
        {
            return new ReplaceMatchResult(false);
        }
        var filePath = resolveMatchResult.Value;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readResult = await FileStorage.ReadAllTextAsync(resource);
            if (readResult.IsFailure)
            {
                return new ReplaceMatchResult(false);
            }
            var content = readResult.Value;

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

            var writeResult = await FileStorage.WriteAllTextAsync(resource, newContent);
            if (writeResult.IsFailure)
            {
                return new ReplaceMatchResult(false);
            }

            return new ReplaceMatchResult(true);
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

    private static string GlobPatternToRegex(string glob)
    {
        var escaped = Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^{escaped}$";
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

        try
        {
            var history = await WorkspaceSettings.GetPropertyAsync<SearchHistory>(SearchHistoryKey);

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
            await WorkspaceSettings.DeletePropertyAsync(SearchHistoryKey);
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
        await WorkspaceSettings.SetPropertyAsync(SearchHistoryKey, updatedHistory);
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
        await WorkspaceSettings.SetPropertyAsync(SearchHistoryKey, updatedHistory);
    }

    public async Task ClearHistoryAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        await WorkspaceSettings.DeletePropertyAsync(SearchHistoryKey);
    }

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
}
