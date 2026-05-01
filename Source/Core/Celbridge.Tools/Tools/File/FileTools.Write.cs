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
    /// <summary>
    /// Writes text content to a file. Creates the file if it does not exist.
    /// For existing files, replaces the entire content. Writes directly to disk.
    /// Any open document reloads its buffer from disk after the write.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write. The file is created automatically if it does not exist.</param>
    /// <param name="content">The new text content for the file.</param>
    /// <returns>JSON object with field: lineCount (int).</returns>
    [McpServerTool(Name = "file_write")]
    [ToolAlias("file.write")]
    public async partial Task<CallToolResult> Write(string fileResource, string content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        var writeResult = await ExecuteCommandAsync<IWriteFileCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Content = content;
        });

        if (writeResult.IsError == true)
        {
            return writeResult;
        }

        var lineCount = LineEndingHelper.CountLines(content);
        var result = new WriteFileResult(lineCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
