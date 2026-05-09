using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_delete_lines with the deleted range, new line count,
/// and context lines around the deletion point for verification.
/// </summary>
public record class DeleteLinesResult(int DeletedFrom, int DeletedTo, int TotalLineCount, List<string>? ContextLines = null);

public partial class FileTools
{
    /// <summary>Delete a 1-based inclusive line range from a text file, including the line terminators.</summary>
    [McpServerTool(Name = "file_delete_lines")]
    [ToolAlias("file.delete_lines")]
    public async partial Task<CallToolResult> DeleteLines(string fileResource, int startLine, int endLine)
    {
        const string ToolGuide = "file_delete_lines";

        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        if (startLine < 1)
        {
            return ToolResponse.Error($"startLine must be at least 1, got {startLine}", ToolGuide);
        }

        if (endLine < startLine)
        {
            return ToolResponse.Error($"endLine ({endLine}) must be greater than or equal to startLine ({startLine})", ToolGuide);
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteLinesCommand>(command =>
        {
            command.Resource = fileResourceKey;
            command.StartLine = startLine;
            command.EndLine = endLine;
        });

        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult, ToolGuide);
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
        return ToolResponse.Success(json);
    }
}
