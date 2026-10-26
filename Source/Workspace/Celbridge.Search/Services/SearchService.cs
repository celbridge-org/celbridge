using System.Diagnostics;
using Celbridge.Automation;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Path = System.IO.Path;

namespace Celbridge.Search.Services;

/// <summary>
/// Service that performs text search across project files.
/// </summary>
public class SearchService : ISearchService, IDisposable
{
    private const int MaxSearchableFileSizeBytes = 1024 * 1024; // 1MB

    private static readonly HashSet<string> ExcludedMetadataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cel",
        ".celbridge"
    };

    // Search landmarks whose spotlight only needs the Search tab activated.
    private static readonly string[] SearchLandmarkIds =
    {
        "search-input",
        "search-run-button",
        "search-history-button",
        "search-match-case-button",
        "search-whole-word-button",
        "search-collapse-results-button",
        "search-replace-toggle-button",
        "search-filters-button",
    };

    // Search landmarks whose spotlight also needs replace mode enabled to reveal the replace controls.
    private static readonly string[] SearchReplaceLandmarkIds =
    {
        "search-replace-input",
        "search-replace-history-button",
        "search-replace-all-button",
    };

    // Search landmarks that also need the search filters shown before a spotlight.
    private static readonly string[] SearchFiltersLandmarkIds =
    {
        "search-files-include-input",
        "search-only-open-documents-button",
        "search-files-exclude-input",
        "search-exclude-settings-button",
    };

    private readonly ILogger<SearchService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ITextBinarySniffer _textBinarySniffer;
    private readonly ISpotlightService _spotlightService;
    private readonly IAutomationService _automationService;
    private readonly ISpotlightLandmark _searchSpotlightLandmark;
    private readonly ISpotlightLandmark _searchReplaceSpotlightLandmark;
    private readonly ISpotlightLandmark _searchFiltersSpotlightLandmark;
    private readonly SearchResultFormatter _formatter;

    // Cancelled when the workspace this service belongs to is torn down. A search runs as a sequence of
    // awaits over the resource layer, so a project reload part way through would otherwise leave it
    // reaching for a workspace that is no longer there.
    private readonly CancellationTokenSource _workspaceCancellation = new();

    // Held separately because a token stays usable after its source is disposed, while reading
    // CancellationTokenSource.Token does not. A search starting inside the teardown window therefore
    // links against an already-cancelled token rather than throwing ObjectDisposedException.
    private readonly CancellationToken _workspaceToken;

    private bool _disposed;

    private IResourceFileSystem ResourceFileSystem => _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
    private IRootHandlerRegistry RootHandlers => _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
    private IResourceWalker ResourceWalker => _workspaceWrapper.WorkspaceService.ResourceService.Walker;
    private IResourcePolicy ResourcePolicy => _workspaceWrapper.WorkspaceService.ResourceService.Policy;
    private IWorkspacePropertyBag PropertyBag => _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag!;

    public SearchService(
        ILogger<SearchService> logger,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ITextBinarySniffer textBinarySniffer,
        ISpotlightService spotlightService,
        IAutomationService automationService)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspaceLoaded);

        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _textBinarySniffer = textBinarySniffer;
        _spotlightService = spotlightService;
        _automationService = automationService;
        _workspaceToken = _workspaceCancellation.Token;
        _formatter = new SearchResultFormatter();

        // Register the Search reveals so spotlighting a Search landmark switches to the Search tab
        // first (and, for the replace controls or the search filters, shows them). Torn down when this
        // workspace-scoped service is disposed.
        _searchSpotlightLandmark = new SearchSpotlightLandmark(workspaceWrapper, SearchLandmarkReveal.SearchTab);
        _searchReplaceSpotlightLandmark = new SearchSpotlightLandmark(workspaceWrapper, SearchLandmarkReveal.ReplaceControls);
        _searchFiltersSpotlightLandmark = new SearchSpotlightLandmark(workspaceWrapper, SearchLandmarkReveal.FilterControls);
        foreach (var landmarkId in SearchLandmarkIds)
        {
            _spotlightService.RegisterLandmark(landmarkId, _searchSpotlightLandmark);
        }
        foreach (var landmarkId in SearchReplaceLandmarkIds)
        {
            _spotlightService.RegisterLandmark(landmarkId, _searchReplaceSpotlightLandmark);
        }
        foreach (var landmarkId in SearchFiltersLandmarkIds)
        {
            _spotlightService.RegisterLandmark(landmarkId, _searchFiltersSpotlightLandmark);
        }
    }

    // Decides whether a walked file should be included in a search, from its size and extension. Internal for
    // the test suite.
    internal bool ShouldSearchFile(WalkedItem file, bool includeMetadataFiles = false)
    {
        if (file.IsFolder
            || file.Size > MaxSearchableFileSizeBytes)
        {
            return false;
        }

        var extension = Path.GetExtension(file.Path).ToLowerInvariant();
        if (_textBinarySniffer.IsBinaryExtension(extension))
        {
            return false;
        }

        if (!includeMetadataFiles
            && ExcludedMetadataExtensions.Contains(extension))
        {
            return false;
        }

        return true;
    }

    private sealed record SearchState
    {
        public int TotalMatches { get; set; }
        public bool ReachedMaxResults { get; set; }
    }

    public async Task<SearchResults> SearchAsync(
        SearchOptions options,
        Action<SearchFileResult>? fileResultFound,
        CancellationToken cancellationToken)
    {
        var fileResults = new List<SearchFileResult>();
        var searchTerm = options.Matcher.Pattern;

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false, 0);
        }

        using var searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _workspaceToken);
        cancellationToken = searchCancellation.Token;

        var projectFolder = _projectService.CurrentProject?.ProjectFolderPath;

        if (string.IsNullOrEmpty(projectFolder))
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false, 0);
        }

        var matcher = options.Matcher;
        var filter = options.Filter;
        var maxResults = options.MaxResults;
        var searchState = new SearchState();
        var searchedFiles = 0;
        var filesMatchingFilters = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // The scope's listing and every file's read and match run on a worker thread, so a search started from
            // the UI thread leaves the UI thread free.
            var searched = await Task.Run(SearchScopeAsync, cancellationToken);
            if (searched)
            {
                var elapsed = (long)stopwatch.Elapsed.TotalMilliseconds;
                _automationService.LogEvent(new SearchCompletedEvent(searchedFiles, elapsed));
            }
        }
        catch (OperationCanceledException)
        {
            return new SearchResults(searchTerm, fileResults, searchState.TotalMatches, fileResults.Count, true, searchState.ReachedMaxResults, filesMatchingFilters);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during search: {ex.Message}");
        }

        return new SearchResults(searchTerm, fileResults, searchState.TotalMatches, fileResults.Count, false, searchState.ReachedMaxResults, filesMatchingFilters);

        // Returns false when the scope names nothing that can be searched.
        async Task<bool> SearchScopeAsync()
        {
            var listResult = options.Files is null
                ? await ListScopeFilesAsync(options.Scope, options.ApplySearchExclude, filter, cancellationToken)
                : await ListNamedFilesAsync(options.Files, options.ApplySearchExclude);
            if (listResult.IsFailure)
            {
                _logger.LogWarning(listResult, $"Failed to list the search scope: '{options.Scope}'");
                return false;
            }

            // The filters match each file's path within the project, whatever the scope.
            var files = listResult.Value
                .Where(file => filter.IsMatch(file.Resource.Path, isFolder: false))
                .ToList();
            filesMatchingFilters = files.Count;

            files.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (maxResults.HasValue
                    && searchState.TotalMatches >= maxResults.Value)
                {
                    searchState.ReachedMaxResults = true;
                    break;
                }

                var remainingMatches = maxResults.HasValue
                    ? maxResults.Value - searchState.TotalMatches
                    : int.MaxValue;

                var fileResult = await SearchFileAsync(
                    file,
                    projectFolder,
                    matcher,
                    remainingMatches,
                    cancellationToken,
                    options.IncludeMetadataFiles);
                searchedFiles++;

                if (fileResult != null
                    && fileResult.Matches.Count > 0)
                {
                    fileResults.Add(fileResult);
                    searchState.TotalMatches += fileResult.Matches.Count;
                    fileResultFound?.Invoke(fileResult);
                }
            }

            return true;
        }
    }

    // Lists the files a search covers: the one file the scope names, or every file inside the scope folder. An empty
    // scope names the project folder, and a bare scope, for example "Data", names "project:Data".
    private async Task<Result<List<WalkedItem>>> ListScopeFilesAsync(
        string scope,
        bool applySearchExclude,
        IPathMatcher filter,
        CancellationToken cancellationToken)
    {
        var scopeKey = ResourceKey.Empty;
        if (!string.IsNullOrEmpty(scope)
            && !ResourceKey.TryCreate(scope, out scopeKey))
        {
            return Result.Fail($"The search scope is not a resource key: '{scope}'");
        }

        var scopeFile = await GetScopeFileAsync(scopeKey);
        if (scopeFile is not null)
        {
            var scopeFiles = new List<WalkedItem>
            {
                scopeFile
            };
            return scopeFiles;
        }

        var walkResult = await ResourceWalker.WalkAsync(scopeKey, applySearchExclude, filter, cancellationToken);
        if (walkResult.IsFailure)
        {
            return Result.Fail($"Failed to walk the search scope: '{scopeKey}'")
                .WithErrors(walkResult);
        }
        var walkedItems = walkResult.Value;

        var files = walkedItems
            .Where(item => !item.IsFolder)
            .ToList();
        return files;
    }

    // Lists the named files that exist, once each. With applySearchExclude, the list leaves out each file excluded by
    // the search list.
    private async Task<Result<List<WalkedItem>>> ListNamedFilesAsync(
        IReadOnlyList<ResourceKey> fileKeys,
        bool applySearchExclude)
    {
        var files = new List<WalkedItem>();
        foreach (var fileKey in fileKeys.Distinct())
        {
            if (applySearchExclude
                && ResourcePolicy.IsSearchExcluded(fileKey, isFolder: false))
            {
                continue;
            }

            var file = await GetScopeFileAsync(fileKey);
            if (file is not null)
            {
                files.Add(file);
            }
        }

        return files;
    }

    // Returns the scope as a walked item when the scope names a file, or null when the scope names anything else.
    private async Task<WalkedItem?> GetScopeFileAsync(ResourceKey scopeKey)
    {
        if (scopeKey.IsEmpty)
        {
            return null;
        }

        var infoResult = await ResourceFileSystem.GetInfoAsync(scopeKey);
        if (infoResult.IsFailure)
        {
            return null;
        }
        var info = infoResult.Value;

        if (info.Kind != StorageItemKind.File)
        {
            return null;
        }

        var resolveResult = RootHandlers.ResolveResourcePath(scopeKey);
        if (resolveResult.IsFailure)
        {
            return null;
        }
        var filePath = resolveResult.Value;

        return new WalkedItem(scopeKey, filePath, false, info.Size, info.ModifiedUtc, info.Attributes);
    }

    private async Task<SearchFileResult?> SearchFileAsync(
        WalkedItem file,
        string rootDirectory,
        ITextMatcher matcher,
        int maxMatches,
        CancellationToken cancellationToken,
        bool includeMetadataFiles)
    {
        var filePath = file.Path;
        var resourceKey = file.Resource;

        try
        {
            // Check if file should be searched (size, extension filters)
            if (!ShouldSearchFile(file, includeMetadataFiles))
            {
                return null;
            }

            // Check if file content is text (not binary) using efficient sampling
            var sniffResult = _textBinarySniffer.IsTextFile(filePath);
            if (!sniffResult.IsSuccess || !sniffResult.Value)
            {
                return null;
            }

            // The workspace can go away at any of this method's await points, which takes the resource
            // layer with it.
            cancellationToken.ThrowIfCancellationRequested();

            // Stream the file via the gateway so reads pick up the same
            // containment validation as writes and large files do not load
            // fully into memory.
            var openResult = await ResourceFileSystem.OpenReadAsync(resourceKey);
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

                    var lineMatches = matcher.FindMatches(line);

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

    private const string SearchHistoryKey = "SearchHistory";
    private const int MaxHistoryItems = 15;

    public async Task<SearchHistory> GetHistoryAsync()
    {
        var emptySearchTerms = new List<string>();
        var emptyReplaceTerms = new List<string>();
        var emptyHistory = new SearchHistory(emptySearchTerms, emptyReplaceTerms);

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return emptyHistory;
        }

        try
        {
            var history = await PropertyBag.GetPropertyAsync<SearchHistory>(SearchHistoryKey);

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
            await PropertyBag.DeletePropertyAsync(SearchHistoryKey);
            return emptyHistory;
        }
    }

    public async Task AddSearchTermToHistoryAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        if (!_workspaceWrapper.IsWorkspaceLoaded)
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
        await PropertyBag.SetPropertyAsync(SearchHistoryKey, updatedHistory);
    }

    public async Task AddReplaceTermToHistoryAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        if (!_workspaceWrapper.IsWorkspaceLoaded)
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
        await PropertyBag.SetPropertyAsync(SearchHistoryKey, updatedHistory);
    }

    public async Task ClearHistoryAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        await PropertyBag.DeletePropertyAsync(SearchHistoryKey);
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
                // Stops an in-flight search before the workspace reference it searches through is cleared.
                _workspaceCancellation.Cancel();
                _workspaceCancellation.Dispose();

                // Dispose managed objects here
                foreach (var landmarkId in SearchLandmarkIds)
                {
                    _spotlightService.UnregisterLandmark(landmarkId);
                }
                foreach (var landmarkId in SearchReplaceLandmarkIds)
                {
                    _spotlightService.UnregisterLandmark(landmarkId);
                }
                foreach (var landmarkId in SearchFiltersLandmarkIds)
                {
                    _spotlightService.UnregisterLandmark(landmarkId);
                }
            }

            _disposed = true;
        }
    }

    ~SearchService()
    {
        Dispose(false);
    }
}
