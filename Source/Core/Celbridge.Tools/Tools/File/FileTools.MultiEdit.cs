using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_multi_edit with the count of applied edits and the
/// post-batch line ranges each replacement occupies. ContextLines is omitted
/// from this surface to keep the payload bounded for large batches.
/// </summary>
public record class FileMultiEditToolResult(int AppliedCount, List<AffectedLineRange> AffectedLines);

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

        var multiEditResult = await ExecuteCommandAsync<IFileMultiEditCommand, FileMultiEditResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Edits = edits;
        });

        if (multiEditResult.IsFailure)
        {
            return ToolResponse.Error(multiEditResult);
        }

        var resultValue = multiEditResult.Value;

        var affectedLines = new List<AffectedLineRange>(resultValue.AffectedRanges.Count);
        foreach (var range in resultValue.AffectedRanges)
        {
            affectedLines.Add(new AffectedLineRange(range.FromLine, range.ToLine));
        }

        var toolResult = new FileMultiEditToolResult(resultValue.AppliedCount, affectedLines);
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
