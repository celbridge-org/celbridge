using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A line range affected by a single edit within a multi-edit batch, tagged
/// with the EditIndex of the input edit that produced it. MatchCount is the
/// number of matches from that edit collapsed into this range. Same-line hits
/// from one edit's replaceAll merge into a single entry with MatchCount
/// reporting the per-line total. Entries from different edits never merge.
/// ContextLines is populated for ranges belonging to non-truncated edits
/// (matching file_edit's verification ergonomics) and omitted for ranges from
/// truncated edits (capped to keep the payload bounded).
/// </summary>
public record class MultiEditAffectedLineRange(int EditIndex, int From, int To, int MatchCount = 1, List<string>? ContextLines = null);

/// <summary>
/// Per-edit summary inside a multi-edit response. MatchCount is the number of
/// matches the edit found at its turn in the sequence; Truncated indicates
/// the edit's contribution to AffectedLines was capped to a sample.
/// </summary>
public record class MultiEditPerEditSummary(int MatchCount, bool Truncated);

/// <summary>
/// Result returned by file_multi_edit. AppliedCount is the number of edits in
/// the batch. Edits carries per-edit MatchCount and Truncated indexed by input
/// edit order. AffectedLines is the flat sorted list of post-batch ranges,
/// each tagged with its originating EditIndex and (for non-truncated edits)
/// ContextLines.
/// </summary>
public record class MultiEditFileToolResult(
    int AppliedCount,
    List<MultiEditPerEditSummary> Edits,
    List<MultiEditAffectedLineRange> AffectedLines);

public partial class FileTools
{
    /// <summary>Apply an atomic batch of text-match edits to a file. All edits land or none do.</summary>
    [McpServerTool(Name = "file_multi_edit")]
    [ToolAlias("file.multi_edit")]
    [RelatedGuides("resource_keys", "editing_documents", "file_changes", "undo_semantics")]
    public async partial Task<CallToolResult> MultiEdit(string fileResource, string editsJson)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        Result<List<FileEditOperation>> parseResult;
        try
        {
            parseResult = ParseMultiEditEditsJson(editsJson);
        }
        catch (JsonException ex)
        {
            return ToolResponse.Error($"Invalid edits JSON: {ex.Message}");
        }

        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }

        var edits = parseResult.Value;

        var multiEditResult = await ExecuteCommandAsync<IMultiEditFileCommand, MultiEditFileResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Edits = edits;
        });

        if (multiEditResult.IsFailure)
        {
            return await WriteFailureResponseAsync(multiEditResult, fileResourceKey);
        }

        var resultValue = multiEditResult.Value;

        var editSummaries = new List<MultiEditPerEditSummary>(resultValue.Edits.Count);
        foreach (var summary in resultValue.Edits)
        {
            editSummaries.Add(new MultiEditPerEditSummary(summary.MatchCount, summary.Truncated));
        }

        // Include contextLines for every returned range, including the
        // first/last sample entries from edits that hit the verbose cap. The
        // cap bounds the payload by entry count. The sample entries are the
        // only verification signal a caller has for a truncated edit, so
        // stripping their context would leave bare positions with no evidence.
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        string[]? fileLines = null;
        if (resultValue.AffectedRanges.Count > 0)
        {
            fileLines = await ReadFileLinesForContextAsync(resourceFileSystem, fileResourceKey);
        }

        var affectedLines = new List<MultiEditAffectedLineRange>(resultValue.AffectedRanges.Count);
        foreach (var range in resultValue.AffectedRanges)
        {
            var contextLines = BuildContextLines(fileLines, range.FromLine, range.ToLine);
            affectedLines.Add(new MultiEditAffectedLineRange(range.EditIndex, range.FromLine, range.ToLine, range.MatchCount, contextLines));
        }

        var toolResult = new MultiEditFileToolResult(resultValue.AppliedCount, editSummaries, affectedLines);
        var json = JsonSerializer.Serialize(toolResult, JsonOptions);
        return ToolResponse.Success(json);
    }

    private static Result<List<FileEditOperation>> ParseMultiEditEditsJson(string editsJson)
    {
        var edits = new List<FileEditOperation>();
        var jsonDocument = JsonDocument.Parse(editsJson);

        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail("Edits JSON must be an array of edit objects");
        }

        var index = 0;
        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("oldString", out var oldStringElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'oldString'");
            }

            if (!element.TryGetProperty("newString", out var newStringElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'newString'");
            }

            var oldString = oldStringElement.GetString() ?? string.Empty;
            var newString = newStringElement.GetString() ?? string.Empty;
            var replaceAll = element.TryGetProperty("replaceAll", out var replaceAllElement)
                && replaceAllElement.ValueKind == JsonValueKind.True;

            edits.Add(new FileEditOperation(oldString, newString, replaceAll));
            index++;
        }

        return edits;
    }
}
