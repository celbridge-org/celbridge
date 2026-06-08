using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A line range affected by a file edit, using 1-based inclusive line numbers.
/// MatchCount is the number of individual matches on this range. When a single
/// replaceAll lands multiple hits on the same line, they collapse into one
/// entry with MatchCount reporting the per-line total. ContextLines contains
/// the post-edit content of the affected lines plus one surrounding line on
/// each side, allowing immediate verification without a follow-up file_read.
/// </summary>
public record class AffectedLineRange(int From, int To, int MatchCount = 1, List<string>? ContextLines = null);

/// <summary>
/// Result returned by file_edit with the count of matches replaced and the
/// post-edit line ranges occupied by each replacement. When MatchCount exceeds
/// the verbose threshold AffectedLines is capped to a first + last sample and
/// Truncated is set to true; MatchCount still reflects the real total.
/// </summary>
public record class EditFileToolResult(int MatchCount, List<AffectedLineRange> AffectedLines, bool Truncated);

public partial class FileTools
{
    /// <summary>Replace an exact text snippet in a file. Snippet must match uniquely unless replaceAll is set.</summary>
    [McpServerTool(Name = "file_edit")]
    [ToolAlias("file.edit")]
    [RelatedGuides("resource_keys", "editing_documents", "file_changes", "undo_semantics")]
    public async partial Task<CallToolResult> Edit(
        string fileResource,
        string oldString,
        string newString,
        bool replaceAll = false)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        var celDenial = DenyWriteToCelTarget(fileResourceKey, fileResource, "file_edit");
        if (celDenial is not null)
        {
            return celDenial;
        }

        var editResult = await ExecuteCommandAsync<IEditFileCommand, EditFileResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.OldString = oldString;
            command.NewString = newString;
            command.ReplaceAll = replaceAll;
        });

        if (editResult.IsFailure)
        {
            return ToolResponse.Error(editResult);
        }

        var editValue = editResult.Value;

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var affectedLines = new List<AffectedLineRange>(editValue.AffectedRanges.Count);

        // ContextLines is included for every returned range, including the
        // first/last sample entries in a truncated response. The cap bounds
        // the payload by entry count. The sample entries are the only
        // verification signal a caller has when truncated, so stripping their
        // context would leave bare positions with no evidence.
        string[]? fileLines = null;
        if (editValue.AffectedRanges.Count > 0)
        {
            fileLines = await ReadFileLinesForContextAsync(resourceFileSystem, fileResourceKey);
        }

        foreach (var range in editValue.AffectedRanges)
        {
            var contextLines = BuildContextLines(fileLines, range.FromLine, range.ToLine);
            affectedLines.Add(new AffectedLineRange(range.FromLine, range.ToLine, range.MatchCount, contextLines));
        }

        var result = new EditFileToolResult(editValue.MatchCount, affectedLines, editValue.Truncated);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
