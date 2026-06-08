using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_replace with the replacement count, the post-edit
/// line ranges where each substitution landed, and a truncated flag for the
/// large-replaceAll case (capped to a first + last sample). ContextLines is
/// included on each range to match file_edit's verification ergonomics.
/// </summary>
public record class ReplaceResult(
    int ReplacementCount,
    List<AffectedLineRange> AffectedLines,
    bool Truncated);

public partial class FileTools
{
    /// <summary>Find and replace text in a file using a literal or regex pattern, optionally within a line range.</summary>
    [McpServerTool(Name = "file_replace")]
    [ToolAlias("file.replace")]
    [RelatedGuides("resource_keys", "regex_syntax", "editing_documents", "file_changes")]
    public async partial Task<CallToolResult> Replace(
        string fileResource,
        string searchText,
        string replaceText,
        bool matchCase = true,
        bool matchWord = false,
        bool useRegex = false,
        int fromLine = 0,
        int toLine = 0)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        var celDenial = ValidateNotCelTarget(fileResourceKey, fileResource, "file_replace");
        if (celDenial is not null)
        {
            return celDenial;
        }

        var findReplaceResult = await ExecuteCommandAsync<IReplaceFileCommand, ReplaceFileResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.MatchWord = matchWord;
            command.UseRegex = useRegex;
            command.FromLine = fromLine;
            command.ToLine = toLine;
        });

        if (findReplaceResult.IsFailure)
        {
            return ToolResponse.Error(findReplaceResult);
        }

        var commandResult = findReplaceResult.Value;

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var affectedLines = new List<AffectedLineRange>(commandResult.AffectedRanges.Count);

        // ContextLines is included for every returned range, including the
        // first/last sample entries in a truncated response. The cap bounds
        // the payload by entry count. The sample entries are the only
        // verification signal a caller has when truncated, so stripping their
        // context would leave bare positions with no evidence.
        string[]? fileLines = null;
        if (commandResult.AffectedRanges.Count > 0)
        {
            fileLines = await ReadFileLinesForContextAsync(resourceFileSystem, fileResourceKey);
        }

        foreach (var range in commandResult.AffectedRanges)
        {
            var contextLines = BuildContextLines(fileLines, range.FromLine, range.ToLine);
            affectedLines.Add(new AffectedLineRange(range.FromLine, range.ToLine, range.MatchCount, contextLines));
        }

        var result = new ReplaceResult(commandResult.ReplacementCount, affectedLines, commandResult.Truncated);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
