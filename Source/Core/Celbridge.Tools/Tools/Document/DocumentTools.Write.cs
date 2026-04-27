using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_write with the line count of the written content.
/// </summary>
public record class WriteDocumentResult(int LineCount);

public partial class DocumentTools
{
    /// <summary>
    /// Writes text content to a document. Creates the file if it does not exist.
    /// For existing files, replaces the entire content. Writes directly to disk.
    /// Any open document reloads its buffer from disk after the write.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write. The file is created automatically if it does not exist.</param>
    /// <param name="content">The new text content for the document.</param>
    /// <returns>JSON object with field: lineCount (int).</returns>
    [McpServerTool(Name = "document_write")]
    [ToolAlias("document.write")]
    public async partial Task<CallToolResult> Write(string fileResource, string content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        var writeResult = await ExecuteCommandAsync<IWriteDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Content = content;
        });

        if (writeResult.IsError == true)
        {
            return writeResult;
        }

        var lineCount = content.Split('\n').Length;
        var result = new WriteDocumentResult(lineCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
