using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_delete_lines with the deleted range, new line count,
/// and context lines around the deletion point for verification.
/// </summary>
public record class DeleteLinesResult(int DeletedFrom, int DeletedTo, int TotalLineCount, List<string>? ContextLines = null);

public partial class DocumentTools
{
    /// <summary>
    /// Deletes complete lines from a document, removing them entirely including their
    /// line terminators. Unlike document_apply_edits with empty newText (which always
    /// leaves a residual empty line), this tool cleanly removes the specified lines.
    /// Writes directly to disk. Any open document reloads its buffer from disk after
    /// the write.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to delete lines from.</param>
    /// <param name="startLine">First line to delete (1-based, inclusive).</param>
    /// <param name="endLine">Last line to delete (1-based, inclusive).</param>
    /// <returns>JSON with fields: deletedFrom (int), deletedTo (int), totalLineCount (int), contextLines (array of strings around the deletion point).</returns>
    [McpServerTool(Name = "document_delete_lines")]
    [ToolAlias("document.delete_lines")]
    public async partial Task<CallToolResult> DeleteLines(string fileResource, int startLine, int endLine)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        if (startLine < 1)
        {
            return ErrorResult($"startLine must be at least 1, got {startLine}");
        }

        if (endLine < startLine)
        {
            return ErrorResult($"endLine ({endLine}) must be greater than or equal to startLine ({startLine})");
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteLinesCommand>(command =>
        {
            command.Resource = fileResourceKey;
            command.StartLine = startLine;
            command.EndLine = endLine;
        });

        if (deleteResult.IsError == true)
        {
            return deleteResult;
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        List<string>? contextLines = null;
        int totalLineCount = 0;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
        if (resolveResult.IsSuccess && File.Exists(resolveResult.Value))
        {
            var fileLines = await File.ReadAllLinesAsync(resolveResult.Value);
            totalLineCount = fileLines.Length;

            // Show a few lines around the deletion point for verification
            var deletionPoint = Math.Min(startLine - 1, fileLines.Length);
            var contextStart = Math.Max(0, deletionPoint - 1);
            var contextEnd = Math.Min(fileLines.Length - 1, deletionPoint + 1);

            if (fileLines.Length > 0)
            {
                contextLines = fileLines
                    .Skip(contextStart)
                    .Take(contextEnd - contextStart + 1)
                    .ToList();
            }
        }

        var result = new DeleteLinesResult(startLine, endLine, totalLineCount, contextLines);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
