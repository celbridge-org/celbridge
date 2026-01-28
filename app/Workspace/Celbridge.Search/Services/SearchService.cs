using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace Celbridge.Search.Services;

/// <summary>
/// Service that performs text search across project files.
/// </summary>
public class SearchService : ISearchService, IDisposable
{
    private const int MaxResults = 1000;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SearchService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly FileFilter _fileFilter;
    private readonly TextMatcher _textMatcher;
    private readonly SearchResultFormatter _formatter;

    private ISearchPanel? _searchPanel;
    public ISearchPanel? SearchPanel => _searchPanel;

    public SearchService(
        IServiceProvider serviceProvider,
        ILogger<SearchService> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _fileFilter = new FileFilter();
        _textMatcher = new TextMatcher();
        _formatter = new SearchResultFormatter();

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _searchPanel = _serviceProvider.GetRequiredService<ISearchPanel>();
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
                _messengerService.UnregisterAll(this);
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

    public async Task<SearchResults> SearchAsync(
        string searchTerm,
        bool matchCase,
        bool wholeWord,
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

                    if (searchState.TotalMatches >= MaxResults)
                    {
                        searchState.ReachedMaxResults = true;
                        break;
                    }

                    var fileResult = SearchFile(
                        filePath,
                        projectFolder,
                        resource,
                        searchTerm,
                        matchCase,
                        wholeWord,
                        MaxResults - searchState.TotalMatches,
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
}
