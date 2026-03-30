using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>
    /// Activates an open document, making it the active tab in the editor.
    /// The document must already be open.
    /// </summary>
    /// <param name="fileResource">Resource key of the document to activate.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "document_activate", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.activate")]
    public async partial Task<CallToolResult> Activate(string fileResource)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        return await ExecuteCommandAsync<IActivateDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
        });
    }
}
