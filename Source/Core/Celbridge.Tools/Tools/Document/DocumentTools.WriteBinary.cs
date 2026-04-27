using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>
    /// Replaces the content of a binary document from base64-encoded data.
    /// Decoded bytes are written directly to disk. Any open document reloads
    /// its buffer from disk after the write.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write.</param>
    /// <param name="base64Content">The new content as a base64-encoded string.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "document_write_binary")]
    [ToolAlias("document.write_binary")]
    public async partial Task<CallToolResult> WriteBinary(string fileResource, string base64Content)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        return await ExecuteCommandAsync<IWriteBinaryDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Base64Content = base64Content;
        });
    }
}
