using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for opening and closing documents in the editor.
/// </summary>
[McpServerToolType]
public partial class DocumentTools : AgentToolBase
{
    public DocumentTools(IApplicationServiceProvider services) : base(services) {}

    /// <summary>
    /// Opens a document in the editor.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to open.</param>
    /// <param name="forceReload">Force reload even if already open.</param>
    [McpServerTool(Name = "document_open", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.open")]
    public async partial Task<CallToolResult> Open(string fileResource, bool forceReload = false)
    {
        return await ExecuteCommandAsync<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceReload = forceReload;
        });
    }

    /// <summary>
    /// Closes a document in the editor.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to close.</param>
    /// <param name="forceClose">Force close without save confirmation.</param>
    [McpServerTool(Name = "document_close", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.close")]
    public async partial Task<CallToolResult> Close(string fileResource, bool forceClose = false)
    {
        return await ExecuteCommandAsync<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceClose = forceClose;
        });
    }
}
