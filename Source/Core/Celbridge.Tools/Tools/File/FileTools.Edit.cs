using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A line range affected by a file edit, using 1-based inclusive line numbers.
/// ContextLines contains the post-edit content of the affected lines plus one
/// surrounding line on each side, allowing immediate verification without a
/// follow-up file_read call.
/// </summary>
public record class AffectedLineRange(int From, int To, List<string>? ContextLines = null);

/// <summary>
/// Result returned by file_edit with the count of matches replaced and the
/// post-edit line ranges occupied by each replacement.
/// </summary>
public record class FileEditToolResult(int MatchCount, List<AffectedLineRange> AffectedLines);

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

        var editResult = await ExecuteCommandAsync<IFileEditCommand, FileEditResult>(command =>
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
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var affectedLines = new List<AffectedLineRange>(editValue.AffectedRanges.Count);

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
        if (resolveResult.IsSuccess && File.Exists(resolveResult.Value))
        {
            var fileLines = await File.ReadAllLinesAsync(resolveResult.Value);

            foreach (var range in editValue.AffectedRanges)
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
            foreach (var range in editValue.AffectedRanges)
            {
                affectedLines.Add(new AffectedLineRange(range.FromLine, range.ToLine));
            }
        }

        var result = new FileEditToolResult(editValue.MatchCount, affectedLines);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
