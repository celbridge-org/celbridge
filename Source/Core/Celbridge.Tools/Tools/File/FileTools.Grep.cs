using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Top-level result returned by file_grep with match totals and per-file results.
/// </summary>
public record class GrepResult(int TotalMatches, int TotalFiles, bool Truncated, List<GrepFileResult> Files);

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

public partial class FileTools
{
    /// <summary>
    /// Maximum serialized JSON length for a successful file_grep response. Sized
    /// to sit comfortably below typical agent-harness truncation thresholds so
    /// oversize results never get spilled to a side file by the harness; the
    /// agent gets a clean GrepOversizeError instead.
    /// </summary>
    private const int MaxGrepResponseChars = 50_000;

    /// <summary>Search file contents by text or regex across the project, with optional context lines.</summary>
    [McpServerTool(Name = "file_grep", ReadOnly = true)]
    [ToolAlias("file.grep")]
    [RelatedGuides("resource_keys", "regex_syntax")]
    public async partial Task<CallToolResult> Grep(string searchTerm, bool useRegex = false, bool matchCase = false, bool wholeWord = false, string include = "", string exclude = "", string resource = "", int maxResults = 100, int contextLines = 0, string files = "", bool includeContent = false, bool summaryOnly = false)
    {
        if (useRegex)
        {
            try
            {
                _ = new Regex(searchTerm);
            }
            catch (ArgumentException ex)
            {
                return ToolResponse.Error($"Invalid regular expression: {ex.Message}");
            }
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        if (!string.IsNullOrEmpty(files))
        {
            return await GrepTargetedFiles(files, searchTerm, useRegex, matchCase, wholeWord, maxResults, contextLines, includeContent, summaryOnly, resourceFileSystem);
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
            resource,
            includeMetadataFiles: true);

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

        var grepResult = new GrepResult(results.TotalMatches, results.TotalFiles, truncated, fileResults);
        return BuildGrepResponse(grepResult);
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
            Hint: "Re-run with summaryOnly:true to probe, then narrow scope via resource, include/exclude, or files.");

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

    private async Task<CallToolResult> GrepTargetedFiles(string filesJson, string searchTerm, bool useRegex, bool matchCase, bool wholeWord, int maxResults, int contextLines, bool includeContent, bool summaryOnly, IResourceFileSystem resourceFileSystem)
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
                var lineMatches = searchRegex.Matches(lineText);

                foreach (Match regexMatch in lineMatches)
                {
                    var lineNumber = lineIdx + 1;

                    if (!summaryOnly)
                    {
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
                    }

                    fileMatchCount++;
                    totalMatches++;
                    if (totalMatches >= maxResults)
                    {
                        truncated = true;
                        break;
                    }
                }
            }

            if (fileMatchCount > 0)
            {
                string? fileContent = null;
                if (includeContent
                    && !summaryOnly)
                {
                    fileContent = string.Join(Environment.NewLine, fileLines);
                }

                fileResults.Add(new GrepFileResult(
                    fileKeyString,
                    fileResourceKey.ResourceName,
                    fileMatchCount,
                    matchList,
                    fileContent));
            }
        }

        var grepResult = new GrepResult(totalMatches, fileResults.Count, truncated, fileResults);
        return BuildGrepResponse(grepResult);
    }
}
