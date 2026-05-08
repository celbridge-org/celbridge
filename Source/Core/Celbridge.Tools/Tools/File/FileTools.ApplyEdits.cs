using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_apply_edits with the affected line ranges and resulting line count.
/// </summary>
public record class ApplyEditsResult(List<AffectedLineRange> AffectedLines, int? TotalLineCount = null);

/// <summary>
/// A line range affected by a file edit, using 1-based line numbers.
/// ContextLines contains the post-edit content of the affected lines plus one
/// surrounding line on each side, allowing immediate verification without a
/// follow-up file_read call.
/// </summary>
public record class AffectedLineRange(int From, int To, List<string>? ContextLines = null);

public partial class FileTools
{
    /// <summary>
    /// Applies targeted text edits at 1-based line/column positions; writes to disk.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to edit.</param>
    /// <param name="editsJson">JSON array of edits with line, column (default 1), endLine, endColumn (default -1 for end of line), and newText.</param>
    /// <returns>JSON object with affectedLines (each carrying post-edit context) and totalLineCount, sufficient for verifying the edit without a follow-up file_read.</returns>
    [McpServerTool(Name = "file_apply_edits")]
    [ToolAlias("file.apply_edits")]
    public async partial Task<CallToolResult> ApplyEdits(string fileResource, string editsJson)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolError($"Invalid resource key: '{fileResource}'");
        }

        Result<List<TextEdit>> parseResult;
        try
        {
            parseResult = ParseEditsJson(editsJson);
        }
        catch (JsonException ex)
        {
            return ToolError($"Invalid edits JSON: {ex.Message}");
        }

        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }

        var textEdits = parseResult.Value;
        if (textEdits.Count == 0)
        {
            return ToolSuccess("ok");
        }

        var fileEdit = new FileEdit(fileResourceKey, textEdits);

        var applyEditsResult = await ExecuteCommandAsync<IApplyEditsCommand, IReadOnlyList<AppliedEdit>>(command =>
        {
            command.Edits = new List<FileEdit> { fileEdit };
        });

        if (applyEditsResult.IsFailure)
        {
            return ToolError(applyEditsResult);
        }

        var appliedEdits = applyEditsResult.Value;

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var affectedLines = new List<AffectedLineRange>();
        int? totalLineCount = null;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
        if (resolveResult.IsSuccess && File.Exists(resolveResult.Value))
        {
            var fileLines = await File.ReadAllLinesAsync(resolveResult.Value);
            totalLineCount = fileLines.Length;

            // Use post-edit ranges from the command. The context window covers
            // one line before through one line after the post-edit range, so
            // multi-line insertions show all of the new content plus surrounding
            // context.
            var orderedRanges = appliedEdits
                .OrderBy(r => r.FromLine)
                .ToList();

            foreach (var range in orderedRanges)
            {
                var contextStartIndex = Math.Max(0, range.FromLine - 2);
                var contextEndIndex = Math.Min(fileLines.Length - 1, range.ToLine);
                var contextLines = fileLines
                    .Skip(contextStartIndex)
                    .Take(contextEndIndex - contextStartIndex + 1)
                    .ToList();
                affectedLines.Add(new AffectedLineRange(range.FromLine, range.ToLine, contextLines));
            }
        }
        else
        {
            foreach (var range in appliedEdits.OrderBy(r => r.FromLine))
            {
                affectedLines.Add(new AffectedLineRange(range.FromLine, range.ToLine));
            }
        }

        var result = new ApplyEditsResult(affectedLines, totalLineCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolSuccess(json);
    }
}
