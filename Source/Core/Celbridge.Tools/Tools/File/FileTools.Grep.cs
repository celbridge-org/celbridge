using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Top-level result returned by file_grep with match totals and per-file results.
/// </summary>
public record class GrepResult(int TotalMatches, int TotalFiles, bool Truncated, List<GrepFileResult> Files);

/// <summary>
/// Per-file result within a file_grep response. Content is included when includeContent is true.
/// </summary>
public record class GrepFileResult(string Resource, string FileName, List<object> Matches, string? Content = null);

/// <summary>
/// A single match within a file_grep result, without context lines.
/// </summary>
public record class GrepMatch(int LineNumber, string LineText, int MatchStart, int MatchLength);

/// <summary>
/// A single match within a file_grep result, with surrounding context lines.
/// </summary>
public record class GrepMatchWithContext(int LineNumber, string LineText, int MatchStart, int MatchLength, List<string> ContextBefore, List<string> ContextAfter);

public partial class FileTools
{
    /// <summary>Search file contents by text or regex across the project, with optional context lines.</summary>
    [McpServerTool(Name = "file_grep", ReadOnly = true)]
    [ToolAlias("file.grep")]
    public async partial Task<CallToolResult> Grep(string searchTerm, bool useRegex = false, bool matchCase = false, bool wholeWord = false, string include = "", string exclude = "", string resource = "", int maxResults = 100, int contextLines = 0, string files = "", bool includeContent = false)
    {
        if (useRegex)
        {
            try
            {
                _ = new Regex(searchTerm);
            }
            catch (ArgumentException ex)
            {
                return ToolError($"Invalid regular expression: {ex.Message}");
            }
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        if (!string.IsNullOrEmpty(files))
        {
            return await GrepTargetedFiles(files, searchTerm, useRegex, matchCase, wholeWord, maxResults, contextLines, includeContent, resourceRegistry);
        }

        var searchService = workspaceWrapper.WorkspaceService.SearchService;

        var results = await searchService.SearchAsync(
            searchTerm,
            matchCase,
            wholeWord,
            maxResults,
            CancellationToken.None,
            useRegex,
            include,
            exclude,
            resource);

        var truncated = results.ReachedMaxResults || results.WasCancelled;

        var fileLineCache = new Dictionary<string, string[]>();

        var fileResults = new List<GrepFileResult>();
        foreach (var fileResult in results.FileResults)
        {
            var matchList = new List<object>();

            foreach (var match in fileResult.Matches)
            {
                if (contextLines > 0)
                {
                    var resolveContextResult = resourceRegistry.ResolveResourcePath(fileResult.Resource);
                    if (resolveContextResult.IsFailure)
                    {
                        matchList.Add(new GrepMatch(match.LineNumber, match.LineText, match.MatchStart, match.MatchLength));
                        continue;
                    }
                    var resourcePath = resolveContextResult.Value;

                    if (!fileLineCache.TryGetValue(resourcePath, out var fileLines))
                    {
                        fileLines = File.Exists(resourcePath) ? await File.ReadAllLinesAsync(resourcePath) : Array.Empty<string>();
                        fileLineCache[resourcePath] = fileLines;
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

            string? fileContent = null;
            if (includeContent)
            {
                var resolveContentResult = resourceRegistry.ResolveResourcePath(fileResult.Resource);
                if (resolveContentResult.IsSuccess && File.Exists(resolveContentResult.Value))
                {
                    fileContent = await File.ReadAllTextAsync(resolveContentResult.Value);
                }
            }

            fileResults.Add(new GrepFileResult(
                fileResult.Resource.ToString(),
                fileResult.FileName,
                matchList,
                fileContent));
        }

        var grepResult = new GrepResult(results.TotalMatches, results.TotalFiles, truncated, fileResults);
        return ToolSuccess(SerializeJson(grepResult));
    }

    private async Task<CallToolResult> GrepTargetedFiles(string filesJson, string searchTerm, bool useRegex, bool matchCase, bool wholeWord, int maxResults, int contextLines, bool includeContent, IResourceRegistry resourceRegistry)
    {
        List<string>? fileKeyStrings;
        try
        {
            fileKeyStrings = JsonSerializer.Deserialize<List<string>>(filesJson);
        }
        catch (JsonException ex)
        {
            return ToolError($"Invalid JSON array for files: {ex.Message}");
        }

        if (fileKeyStrings is null || fileKeyStrings.Count == 0)
        {
            return ToolError("No resource keys provided in files parameter.");
        }

        var searchPattern = useRegex ? searchTerm : Regex.Escape(searchTerm);
        if (wholeWord && !useRegex)
        {
            searchPattern = $@"\b{searchPattern}\b";
        }

        var regexOptions = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var searchRegex = new Regex(searchPattern, regexOptions);

        var fileResults = new List<GrepFileResult>();
        int totalMatches = 0;
        bool truncated = false;

        foreach (var fileKeyString in fileKeyStrings)
        {
            if (truncated)
            {
                break;
            }

            if (!ResourceKey.TryCreate(fileKeyString, out var fileResourceKey))
            {
                continue;
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
            if (resolveResult.IsFailure)
            {
                continue;
            }
            var filePath = resolveResult.Value;

            if (!File.Exists(filePath))
            {
                continue;
            }

            var fileLines = await File.ReadAllLinesAsync(filePath);
            var matchList = new List<object>();

            for (int lineIdx = 0; lineIdx < fileLines.Length && !truncated; lineIdx++)
            {
                var lineText = fileLines[lineIdx];
                var lineMatches = searchRegex.Matches(lineText);

                foreach (Match regexMatch in lineMatches)
                {
                    var lineNumber = lineIdx + 1;

                    if (contextLines > 0)
                    {
                        var contextBeforeStart = Math.Max(0, lineIdx - contextLines);
                        var contextAfterEnd = Math.Min(fileLines.Length - 1, lineIdx + contextLines);

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
                            regexMatch.Index,
                            regexMatch.Length,
                            contextBefore,
                            contextAfter));
                    }
                    else
                    {
                        matchList.Add(new GrepMatch(lineNumber, lineText, regexMatch.Index, regexMatch.Length));
                    }

                    totalMatches++;
                    if (totalMatches >= maxResults)
                    {
                        truncated = true;
                        break;
                    }
                }
            }

            if (matchList.Count > 0)
            {
                string? fileContent = null;
                if (includeContent)
                {
                    fileContent = string.Join(Environment.NewLine, fileLines);
                }

                fileResults.Add(new GrepFileResult(
                    fileKeyString,
                    Path.GetFileName(filePath),
                    matchList,
                    fileContent));
            }
        }

        var grepResult = new GrepResult(totalMatches, fileResults.Count, truncated, fileResults);
        return ToolSuccess(SerializeJson(grepResult));
    }
}
