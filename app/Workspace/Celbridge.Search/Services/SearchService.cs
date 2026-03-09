using Celbridge.Logging;
using Celbridge.Workspace;
using Path = System.IO.Path;

namespace Celbridge.Search.Services;

/// <summary>
/// Service that performs text search across project files.
/// </summary>
public class SearchService : ISearchService, IDisposable
{
    private const int MaxResults = 1000;

    private readonly ILogger<SearchService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly FileFilter _fileFilter;
    private readonly TextMatcher _textMatcher;
    private readonly SearchResultFormatter _formatter;

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
        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            return new ReplaceResult(false, 0);
        }

        var lines = content.Split('\n');
        var replacedLines = new List<string>();
        int totalReplacements = 0;

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = rawLine.TrimEnd('\r');
            var carriageReturn = rawLine.EndsWith('\r') ? "\r" : string.Empty;

            var matches = _textMatcher.FindMatches(line, searchText, matchCase, wholeWord);

            if (matches.Count == 0)
            {
                replacedLines.Add(rawLine);
                continue;
            }

            // Replace matches from end to start to preserve indices
            var modifiedLine = line;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                modifiedLine = modifiedLine.Substring(0, match.Start) +
                              replaceText +
                              modifiedLine.Substring(match.Start + match.Length);
                totalReplacements++;
            }

            replacedLines.Add(modifiedLine + carriageReturn);
        }

        if (totalReplacements == 0)
        {
            return new ReplaceResult(true, 0);
        }

        var newContent = string.Join("\n", replacedLines);

        try
        {
            File.WriteAllText(filePath, newContent);
        }
        catch (IOException ex)
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
        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (IOException)
        {
            return new ReplaceMatchResult(false);
        }

        var lines = content.Split('\n');
        var lineIndex = lineNumber - 1;

        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return new ReplaceMatchResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rawLine = lines[lineIndex];
        var line = rawLine.TrimEnd('\r');
        var carriageReturn = rawLine.EndsWith('\r') ? "\r" : string.Empty;

        // Find the specific match at the expected position
        var matches = _textMatcher.FindMatches(line, searchText, matchCase, wholeWord);
        var targetMatch = matches.FirstOrDefault(m => m.Start == originalMatchStart);

        if (targetMatch == default)
        {
            return new ReplaceMatchResult(false);
        }

        var modifiedLine = line.Substring(0, targetMatch.Start) +
                           replaceText +
                           line.Substring(targetMatch.Start + targetMatch.Length);
        lines[lineIndex] = modifiedLine + carriageReturn;

        var newContent = string.Join("\n", lines);

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
}
