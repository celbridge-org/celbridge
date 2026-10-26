using System.Text.Json;
using Celbridge.Search;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Top-level result returned by file_grep with match totals and per-file results. Warning is set when the include
/// patterns matched no file.
/// </summary>
public record class GrepResult(int TotalMatches, int TotalFiles, bool Truncated, List<GrepFileResult> Files, string? Warning = null);

/// <summary>
/// Per-file result within a file_grep response. MatchCount is always populated; Matches is empty
/// when summaryOnly is true. Content is included when includeContent is true.
/// </summary>
public record class GrepFileResult(string Resource, string FileName, int MatchCount, List<object> Matches, string? Content = null);

/// <summary>
/// A single match within a file_grep result, without context lines.
/// </summary>
public record class GrepMatch(int LineNumber, string LineText, int MatchStart, int MatchLength);

/// <summary>
/// A single match within a file_grep result, with surrounding context lines.
/// </summary>
public record class GrepMatchWithContext(int LineNumber, string LineText, int MatchStart, int MatchLength, List<string> ContextBefore, List<string> ContextAfter);

/// <summary>
/// Returned in place of a full GrepResult when the serialized response would
/// exceed the per-call character cap. Carries totals so the agent can gauge
/// scale, and a hint pointing at summaryOnly and scope-narrowing parameters.
/// </summary>
public record class GrepOversizeError(string Error, int TotalMatches, int TotalFiles, int ResponseChars, int LimitChars, string Hint);

/// <summary>
/// Sets how many matches file_grep returns, and the context and content that come with each one.
/// </summary>
internal sealed record GrepOutputOptions(int MaxResults, int ContextLines, bool IncludeContent, bool SummaryOnly);

public partial class FileTools
{
    /// <summary>
    /// Maximum serialized JSON length for a successful file_grep response. Sized
    /// to sit comfortably below typical agent-harness truncation thresholds so
    /// oversize results never get spilled to a side file by the harness; the
    /// agent gets a clean GrepOversizeError instead.
    /// </summary>
    private const int MaxGrepResponseChars = 50_000;

    /// <summary>Search file contents with a ripgrep regex or plain text, with optional context lines.</summary>
    [McpServerTool(Name = "file_grep", ReadOnly = true)]
    [ToolAlias("file.grep")]
    [RelatedGuides("resource_keys", "regex_syntax", "search_filters")]
    public async partial Task<CallToolResult> Grep(string pattern, bool literal = false, bool matchCase = true, bool wholeWord = false, string path = "", string include = "", string exclude = "", int maxResults = 100, int contextLines = 0, string files = "", bool includeContent = false, bool summaryOnly = false, bool applySearchExclude = true)
    {
        var patternOptions = new SearchPatternOptions(literal, matchCase, wholeWord, Multiline: false);
        var patternResult = SearchPattern.Compile(pattern, patternOptions);
        if (patternResult.IsFailure)
        {
            return ToolResponse.Error(patternResult);
        }
        var searchPattern = patternResult.Value;

        var filterResult = SearchFilter.Compile(include, exclude);
        if (filterResult.IsFailure)
        {
            return ToolResponse.Error(filterResult);
        }
        var filter = filterResult.Value;

        var output = new GrepOutputOptions(maxResults, contextLines, includeContent, summaryOnly);

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceService = workspaceWrapper.WorkspaceService.ResourceService;
        var resourceFileSystem = resourceService.FileSystem;

        if (!string.IsNullOrEmpty(files))
        {
            return await GrepTargetedFiles(files, searchPattern, output, resourceFileSystem);
        }

        // A path that names nothing fails the call. Otherwise the result would look like a search that found no match.
        var scopeKey = ResourceKey.Empty;
        if (!string.IsNullOrEmpty(path))
        {
            if (!ResourceKey.TryCreate(path, out scopeKey))
            {
                return ToolResponse.InvalidResourceKey(path);
            }

            if (!scopeKey.IsEmpty)
            {
                var scopeInfoResult = await resourceFileSystem.GetInfoAsync(scopeKey);
                if (scopeInfoResult.IsFailure
                    || scopeInfoResult.Value.Kind == StorageItemKind.NotFound)
                {
                    return ToolResponse.ResourceNotFound(path);
                }
            }
        }

        // A path under another root (temp:, logs:) is listed here and matched the same way the files= path is.
        // A bare or project: path goes to SearchService.
        var rootHandlerRegistry = resourceService.RootHandlers;
        if (scopeKey.Root != ResourceKey.DefaultRoot
            && rootHandlerRegistry.RootHandlers.ContainsKey(scopeKey.Root))
        {
            return await GrepNonDefaultRootScopeAsync(scopeKey, searchPattern, filter, include, output, resourceService);
        }

        var searchService = workspaceWrapper.WorkspaceService.SearchService;

        var searchOptions = new SearchOptions(searchPattern, filter)
        {
            MaxResults = maxResults,
            Scope = path,
            IncludeMetadataFiles = true,
            ApplySearchExclude = applySearchExclude,
        };
        var results = await searchService.SearchAsync(searchOptions, CancellationToken.None);

        var truncated = results.ReachedMaxResults || results.WasCancelled;

        var fileLineCache = new Dictionary<ResourceKey, string[]>();

        var fileResults = new List<GrepFileResult>();
        foreach (var fileResult in results.FileResults)
        {
            var matchCount = fileResult.Matches.Count;
            var matchList = new List<object>();

            if (!summaryOnly)
            {
                foreach (var match in fileResult.Matches)
                {
                    if (contextLines > 0)
                    {
                        if (!fileLineCache.TryGetValue(fileResult.Resource, out var fileLines))
                        {
                            fileLines = await ReadFileLinesStreamedAsync(resourceFileSystem, fileResult.Resource);
                            fileLineCache[fileResult.Resource] = fileLines;
                        }

                        var matchLineIndex = match.LineNumber - 1;
                        var contextBeforeStart = Math.Max(0, matchLineIndex - contextLines);
                        var contextAfterEnd = Math.Min(fileLines.Length - 1, matchLineIndex + contextLines);

                        var contextBefore = new List<string>();
                        for (int i = contextBeforeStart; i < matchLineIndex; i++)
                        {
                            contextBefore.Add(fileLines[i]);
                        }

                        var contextAfter = new List<string>();
                        for (int i = matchLineIndex + 1; i <= contextAfterEnd; i++)
                        {
                            contextAfter.Add(fileLines[i]);
                        }

                        matchList.Add(new GrepMatchWithContext(
                            match.LineNumber,
                            match.LineText,
                            match.MatchStart,
                            match.MatchLength,
                            contextBefore,
                            contextAfter));
                    }
                    else
                    {
                        matchList.Add(new GrepMatch(
                            match.LineNumber,
                            match.LineText,
                            match.MatchStart,
                            match.MatchLength));
                    }
                }
            }

            string? fileContent = null;
            if (includeContent
                && !summaryOnly)
            {
                var contentResult = await resourceFileSystem.ReadAllTextAsync(fileResult.Resource);
                if (contentResult.IsSuccess)
                {
                    fileContent = contentResult.Value;
                }
            }

            fileResults.Add(new GrepFileResult(
                fileResult.Resource.ToString(),
                fileResult.FileName,
                matchCount,
                matchList,
                fileContent));
        }

        var warning = IncludeWarning(filter, include, results.FilesMatchingFilters);
        var grepResult = new GrepResult(results.TotalMatches, results.TotalFiles, truncated, fileResults, warning);
        return BuildGrepResponse(grepResult);
    }

    // Returns a warning when the include patterns matched no file, so a wrong filter shows in the result.
    private static string? IncludeWarning(SearchFilter filter, string include, int filesMatchingFilters)
    {
        if (!filter.HasIncludePatterns
            || filesMatchingFilters > 0)
        {
            return null;
        }

        return $"No file matched the include patterns '{include}'. A pattern with no '/' matches a name at any depth, like '*.py'. A pattern with a '/' is relative to the root, like 'src/**/*.py', even when path names a folder.";
    }

    private static CallToolResult BuildGrepResponse(GrepResult grepResult)
    {
        var json = SerializeJson(grepResult);
        if (json.Length <= MaxGrepResponseChars)
        {
            return ToolResponse.Success(json);
        }

        var oversizeError = new GrepOversizeError(
            Error: "result_too_large",
            TotalMatches: grepResult.TotalMatches,
            TotalFiles: grepResult.TotalFiles,
            ResponseChars: json.Length,
            LimitChars: MaxGrepResponseChars,
            Hint: "Re-run with summaryOnly:true to probe, then narrow scope via path, include/exclude, or files.");

        return new CallToolResult
        {
            IsError = true,
            Content = [
                new TextContentBlock
                {
                    Text = SerializeJson(oversizeError)
                }
            ],
        };
    }

    /// <summary>
    /// Streams a file via the gateway's OpenReadAsync and returns it as a
    /// line array. Avoids loading the full content into memory and routes the
    /// read through containment validation. Returns an empty array on failure
    /// so callers can treat missing or unreadable files as zero matches.
    /// </summary>
    private static async Task<string[]> ReadFileLinesStreamedAsync(IResourceFileSystem resourceFileSystem, ResourceKey resource)
    {
        var openResult = await resourceFileSystem.OpenReadAsync(resource);
        if (openResult.IsFailure)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        await using var stream = openResult.Value;
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            lines.Add(line);
        }
        return lines.ToArray();
    }

    private async Task<CallToolResult> GrepTargetedFiles(string filesJson, SearchPattern searchPattern, GrepOutputOptions output, IResourceFileSystem resourceFileSystem)
    {
        // Detect the most common mis-use: a glob or single path passed where a
        // JSON array is required. The raw JsonException for this case ("'w' is
        // an invalid start of a value") tells the caller something is wrong
        // but not what to type instead.
        var trimmedFilesJson = filesJson.TrimStart();
        if (!trimmedFilesJson.StartsWith('['))
        {
            return ToolResponse.Error(
                "files takes a JSON array of resource keys, e.g. [\"folder/a.txt\",\"folder/b.txt\"]. " +
                "For glob-based scoping, use the include parameter instead.");
        }

        List<string>? fileKeyStrings;
        try
        {
            fileKeyStrings = JsonSerializer.Deserialize<List<string>>(filesJson);
        }
        catch (JsonException)
        {
            return ToolResponse.Error(
                "files takes a JSON array of resource keys, e.g. [\"folder/a.txt\",\"folder/b.txt\"]. " +
                "For glob-based scoping, use the include parameter instead.");
        }

        if (fileKeyStrings is null || fileKeyStrings.Count == 0)
        {
            return ToolResponse.Error("No resource keys provided in files parameter.");
        }

        // The result's Resource field echoes the caller's exact key string, so a
        // round-trip through ResourceKey does not canonicalize what the agent passed.
        var targets = new List<(ResourceKey Key, string Display)>();
        foreach (var fileKeyString in fileKeyStrings)
        {
            if (ResourceKey.TryCreate(fileKeyString, out var fileResourceKey))
            {
                targets.Add((fileResourceKey, fileKeyString));
            }
        }

        var grepResult = await GrepFileListAsync(targets, searchPattern, output, resourceFileSystem);
        return BuildGrepResponse(grepResult);
    }

    // Lists a path under another root (temp:, logs:): the one file the path names, or the files inside the folder.
    // The files are matched the same way the files= path matches. The filter matches each file's path within its
    // root.
    private async Task<CallToolResult> GrepNonDefaultRootScopeAsync(
        ResourceKey scopeKey,
        SearchPattern searchPattern,
        SearchFilter filter,
        string include,
        GrepOutputOptions output,
        IResourceService resourceService)
    {
        // If the path is a file, grep that file. Otherwise grep every file in the folder. A failed walk finds no
        // files.
        var fileKeys = new List<ResourceKey>();
        var scopeIsFile = false;
        var scopeInfoResult = await resourceService.FileSystem.GetInfoAsync(scopeKey);
        if (scopeInfoResult.IsSuccess)
        {
            var scopeInfo = scopeInfoResult.Value;
            scopeIsFile = scopeInfo.Kind == StorageItemKind.File;
        }

        if (scopeIsFile)
        {
            fileKeys.Add(scopeKey);
        }
        else
        {
            var walkResult = await resourceService.Walker.WalkAsync(scopeKey);
            if (walkResult.IsSuccess)
            {
                var walkedItems = walkResult.Value;
                var walkedFiles = walkedItems
                    .Where(item => !item.IsFolder)
                    .Select(item => item.Resource);
                fileKeys.AddRange(walkedFiles);
            }
        }

        var targets = fileKeys
            .Where(fileKey => filter.IsMatch(fileKey.Path, isFolder: false))
            .Select(fileKey => (fileKey, fileKey.ToString()))
            .ToList();

        var grepResult = await GrepFileListAsync(targets, searchPattern, output, resourceService.FileSystem);
        var warning = IncludeWarning(filter, include, targets.Count);

        return BuildGrepResponse(grepResult with { Warning = warning });
    }

    // Greps an explicit list of files, accumulating matches up to maxResults.
    // Shared by the targeted files= path and the non-default-root scope walk;
    // each target carries the display string used for its result Resource field.
    private async Task<GrepResult> GrepFileListAsync(
        List<(ResourceKey Key, string Display)> targets,
        SearchPattern searchPattern,
        GrepOutputOptions output,
        IResourceFileSystem resourceFileSystem)
    {
        var fileResults = new List<GrepFileResult>();
        int totalMatches = 0;
        bool truncated = false;

        foreach (var target in targets)
        {
            if (truncated)
            {
                break;
            }

            var fileResourceKey = target.Key;

            var infoResult = await resourceFileSystem.GetInfoAsync(fileResourceKey);
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                continue;
            }

            var fileLines = await ReadFileLinesStreamedAsync(resourceFileSystem, fileResourceKey);
            var matchList = new List<object>();
            int fileMatchCount = 0;

            for (int lineIdx = 0; lineIdx < fileLines.Length && !truncated; lineIdx++)
            {
                var lineText = fileLines[lineIdx];
                var lineMatches = searchPattern.FindMatches(lineText);

                foreach (var lineMatch in lineMatches)
                {
                    var lineNumber = lineIdx + 1;

                    if (!output.SummaryOnly)
                    {
                        if (output.ContextLines > 0)
                        {
                            var contextBeforeStart = Math.Max(0, lineIdx - output.ContextLines);
                            var contextAfterEnd = Math.Min(fileLines.Length - 1, lineIdx + output.ContextLines);

                            var contextBefore = new List<string>();
                            for (int i = contextBeforeStart; i < lineIdx; i++)
                            {
                                contextBefore.Add(fileLines[i]);
                            }

                            var contextAfter = new List<string>();
                            for (int i = lineIdx + 1; i <= contextAfterEnd; i++)
                            {
                                contextAfter.Add(fileLines[i]);
                            }

                            matchList.Add(new GrepMatchWithContext(
                                lineNumber,
                                lineText,
                                lineMatch.Start,
                                lineMatch.Length,
                                contextBefore,
                                contextAfter));
                        }
                        else
                        {
                            matchList.Add(new GrepMatch(lineNumber, lineText, lineMatch.Start, lineMatch.Length));
                        }
                    }

                    fileMatchCount++;
                    totalMatches++;
                    if (totalMatches >= output.MaxResults)
                    {
                        truncated = true;
                        break;
                    }
                }
            }

            if (fileMatchCount > 0)
            {
                string? fileContent = null;
                if (output.IncludeContent
                    && !output.SummaryOnly)
                {
                    fileContent = string.Join(Environment.NewLine, fileLines);
                }

                fileResults.Add(new GrepFileResult(
                    target.Display,
                    fileResourceKey.ResourceName,
                    fileMatchCount,
                    matchList,
                    fileContent));
            }
        }

        return new GrepResult(totalMatches, fileResults.Count, truncated, fileResults);
    }
}
