using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_write with the line count of the written content.
/// </summary>
public record class WriteFileResult(int LineCount);

public partial class FileTools
{
    /// <summary>Wholesale-replace a text file with new content, creating it if missing.</summary>
    [McpServerTool(Name = "file_write", Idempotent = true)]
    [ToolAlias("file.write")]
    [RelatedGuides("resource_keys", "editing_documents", "file_changes")]
    public async partial Task<CallToolResult> Write(string fileResource, string content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        var celDenial = DenyWriteToCelTarget(fileResourceKey, fileResource, "file_write");
        if (celDenial is not null)
        {
            return celDenial;
        }

        var writeResult = await ExecuteCommandAsync<IWriteFileCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Content = content;
        });

        if (writeResult.IsFailure)
        {
            return ToolResponse.Error(writeResult);
        }

        var lineCount = LineEndingHelper.CountLines(content);
        var result = new WriteFileResult(lineCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
