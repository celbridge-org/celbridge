using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>
    /// Replaces the content of a binary document from base64-encoded data.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write.</param>
    /// <param name="base64Content">The new content as a base64-encoded string.</param>
    /// <param name="openDocument">When true (default), opens the document in the editor. When false and document is not already open, writes decoded bytes directly to disk.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "document_write_binary")]
    [ToolAlias("document.write_binary")]
    public async partial Task<CallToolResult> WriteBinary(string fileResource, string base64Content, bool openDocument = true)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        return await ExecuteCommandAsync<IWriteBinaryDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Base64Content = base64Content;
            command.OpenDocument = openDocument;
        });
    }
}
