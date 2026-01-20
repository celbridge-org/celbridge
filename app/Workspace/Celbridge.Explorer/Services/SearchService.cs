using Celbridge.Logging;
using Celbridge.Workspace;
using System.Text;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Service that performs text search across project files.
/// </summary>
public class SearchService : ISearchService
{
    // Maximum file size to search (1MB)
    private const int MaxFileSizeBytes = 1024 * 1024;

    // Maximum number of results to return
    private const int MaxResults = 1000;

    // Maximum characters before the match in the display text
    private const int MaxPrefixChars = 30;

    // Maximum total display length
    private const int MaxDisplayLength = 100;

    private readonly ILogger<SearchService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SearchService(
        ILogger<SearchService> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
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

        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
        var resourceRegistry = explorerService.ResourceRegistry;
        var projectFolder = resourceRegistry.ProjectFolderPath;

        if (string.IsNullOrEmpty(projectFolder) || 
            !Directory.Exists(projectFolder))
        {
            return new SearchResults(searchTerm, fileResults, 0, 0, false, false);
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
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
                        comparison,
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
        StringComparison comparison,
        int maxMatches,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);

            // Skip if file doesn't exist
            if (!fileInfo.Exists)
            {
                return null;
            }

            // Skip large files
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return null;
            }

            // Skip binary and metadata files based on extension
            if (IsExcludedFile(filePath))
            {
                return null;
            }

            // Try to read the file and check if it's text
            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch
            {
                return null;
            }

            // Quick check if content contains null characters (binary indicator)
            if (content.Contains('\0'))
            {
                return null;
            }

            var matches = new List<SearchMatchLine>();
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length && matches.Count < maxMatches; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = lines[i].TrimEnd('\r');
                var lineMatches = FindMatches(line, searchTerm, matchCase, wholeWord, comparison);

                foreach (var match in lineMatches)
                {
                    if (matches.Count >= maxMatches)
                        break;

                    var (contextLine, displayMatchStart) = GetContextLine(line, match.Start, match.Length);
                    matches.Add(new SearchMatchLine(
                        i + 1, // Line numbers are 1-based
                        contextLine,
                        displayMatchStart,
                        match.Length));
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

    private List<(int Start, int Length)> FindMatches(
        string line,
        string searchTerm,
        bool matchCase,
        bool wholeWord,
        StringComparison comparison)
    {
        var matches = new List<(int Start, int Length)>();
        int index = 0;

        while (index < line.Length)
        {
            int foundIndex = line.IndexOf(searchTerm, index, comparison);
            if (foundIndex < 0)
            {
                break;
            }

            bool isMatch = true;

            if (wholeWord)
            {
                // Check if it's a whole word match
                bool startOk = foundIndex == 0 || !char.IsLetterOrDigit(line[foundIndex - 1]);
                bool endOk = foundIndex + searchTerm.Length >= line.Length ||
                             !char.IsLetterOrDigit(line[foundIndex + searchTerm.Length]);
                isMatch = startOk && endOk;
            }

            if (isMatch)
            {
                matches.Add((foundIndex, searchTerm.Length));
            }

            index = foundIndex + 1;
        }

        return matches;
    }

    /// <summary>
    /// Creates a context line for display, ensuring the match appears within the first ~30 characters.
    /// Returns the display text and the adjusted match start position within that text.
    /// </summary>
    private (string DisplayText, int MatchStart) GetContextLine(string line, int matchStart, int matchLength)
    {
        // First, trim leading whitespace and track the offset
        int leadingWhitespace = 0;
        while (leadingWhitespace < line.Length && char.IsWhiteSpace(line[leadingWhitespace]))
        {
            leadingWhitespace++;
        }
        
        var trimmedLine = line.Substring(leadingWhitespace).TrimEnd();
        
        // Adjust match position for trimmed leading whitespace
        int adjustedMatchStart = matchStart - leadingWhitespace;
        
        // Clamp to valid range
        adjustedMatchStart = Math.Clamp(adjustedMatchStart, 0, Math.Max(0, trimmedLine.Length - 1));
        
        // If the trimmed line is short AND the match is near the start, return the whole line
        if (trimmedLine.Length <= MaxDisplayLength && adjustedMatchStart <= MaxPrefixChars)
        {
            return (trimmedLine, adjustedMatchStart);
        }

        // Calculate context start: ensure match is within first MaxPrefixChars characters of the displayed text
        int contextStart = 0;
        bool needsPrefix = false;
        
        if (adjustedMatchStart > MaxPrefixChars)
        {
            // Need to skip some text to bring the match closer to the start
            // Target: place the match at around MaxPrefixChars position in the output
            contextStart = adjustedMatchStart - MaxPrefixChars;
            
            // Look for a word boundary to break at (search forward from contextStart)
            int wordBoundary = FindWordBoundaryForward(trimmedLine, contextStart);
            if (wordBoundary > 0 && wordBoundary <= adjustedMatchStart)
            {
                contextStart = wordBoundary;
            }
            
            needsPrefix = true;
        }
        
        // Calculate available length for content
        int availableLength = MaxDisplayLength;
        if (needsPrefix)
        {
            availableLength -= 3; // Reserve space for "..."
        }
        
        // Calculate context end
        int contextEnd = Math.Min(trimmedLine.Length, contextStart + availableLength);
        bool needsSuffix = contextEnd < trimmedLine.Length;
        
        if (needsSuffix)
        {
            availableLength -= 3; // Reserve space for "..."
            contextEnd = Math.Min(trimmedLine.Length, contextStart + availableLength);
            
            // Try to break at a word boundary, but ensure we don't cut off the match
            int matchEndInTrimmed = adjustedMatchStart + matchLength;
            int wordBoundary = FindWordBoundaryBackward(trimmedLine, contextEnd);
            if (wordBoundary > matchEndInTrimmed && wordBoundary > contextStart)
            {
                contextEnd = wordBoundary;
            }
        }
        
        // Ensure valid bounds
        contextStart = Math.Max(0, contextStart);
        contextEnd = Math.Min(trimmedLine.Length, contextEnd);
        
        if (contextEnd <= contextStart)
        {
            // Fallback: just show from match position
            contextStart = adjustedMatchStart;
            contextEnd = Math.Min(trimmedLine.Length, contextStart + availableLength);
            needsPrefix = contextStart > 0;
            needsSuffix = contextEnd < trimmedLine.Length;
        }
        
        // Build the result
        var result = new StringBuilder();
        
        if (needsPrefix)
        {
            result.Append("...");
        }
        
        result.Append(trimmedLine.AsSpan(contextStart, contextEnd - contextStart));
        
        if (needsSuffix)
        {
            result.Append("...");
        }
        
        // Calculate the match position in the display text
        int displayMatchStart = adjustedMatchStart - contextStart;
        if (needsPrefix)
        {
            displayMatchStart += 3; // Account for "..." prefix
        }
        
        // Clamp to valid range within display text
        displayMatchStart = Math.Clamp(displayMatchStart, 0, Math.Max(0, result.Length - 1));
        
        return (result.ToString(), displayMatchStart);
    }

    /// <summary>
    /// Finds the position of the nearest word boundary at or after the given position.
    /// Uses Unicode-aware character classification.
    /// Returns the position after the boundary character, or -1 if not found.
    /// </summary>
    private static int FindWordBoundaryForward(string text, int position)
    {
        if (position < 0 || position >= text.Length)
        {
            return -1;
        }
        
        // Look for whitespace or punctuation that indicates a word boundary
        for (int i = position; i < text.Length && i < position + 20; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSeparator(c))
            {
                // Skip consecutive whitespace/punctuation and return position of next content
                int j = i + 1;
                while (j < text.Length && (char.IsWhiteSpace(text[j]) || char.IsPunctuation(text[j]) || char.IsSeparator(text[j])))
                {
                    j++;
                }
                return j < text.Length ? j : -1;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// Finds the position of the nearest word boundary at or before the given position.
    /// Uses Unicode-aware character classification.
    /// Returns -1 if no suitable boundary is found.
    /// </summary>
    private static int FindWordBoundaryBackward(string text, int position)
    {
        if (position <= 0 || position > text.Length)
        {
            return -1;
        }
        
        // Look for whitespace or punctuation that indicates a word boundary
        for (int i = position - 1; i >= 0 && i >= position - 20; i--)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSeparator(c))
            {
                // Return position after the boundary character
                return i + 1;
            }
        }
        
        return -1;
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

    private bool IsExcludedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Metadata files to exclude from search
        var metadataExtensions = new HashSet<string>
        {
            ".webapp",
            ".celbridge"
        };

        if (metadataExtensions.Contains(extension))
        {
            return true;
        }

        // Common binary extensions to skip
        var binaryExtensions = new HashSet<string>
        {
            ".exe", ".dll", ".pdb", ".obj", ".o", ".a", ".lib",
            ".so", ".dylib", ".bin", ".dat",
            ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2",
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
            ".mp3", ".wav", ".ogg", ".flac", ".aac",
            ".mp4", ".avi", ".mkv", ".mov", ".webm",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            ".pyc", ".pyo", ".class",
            ".db", ".sqlite", ".sqlite3",
            ".nupkg", ".snupkg",
            ".vsix", ".msi", ".cab"
        };

        return binaryExtensions.Contains(extension);
    }
}
