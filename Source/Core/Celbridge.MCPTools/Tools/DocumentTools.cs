using Celbridge.Commands;
using Celbridge.Documents;
using ModelContextProtocol.Server;

namespace Celbridge.MCPTools.Tools;

/// <summary>
/// MCP tools for opening and closing documents in the editor.
/// </summary>
[McpServerToolType]
public class DocumentTools
{
    private readonly ICommandService _commandService;

    public DocumentTools(ICommandService commandService)
    {
        _commandService = commandService;
    }

    /// <summary>
    /// Opens a document in the editor.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to open.</param>
    /// <param name="forceReload">Force reload even if already open.</param>
    [McpServerTool(Name = "document_open", ReadOnly = false, Idempotent = true)]
    [ToolAlias("open")]
    public void Open(string fileResource, bool forceReload = false)
    {
        _commandService.Execute<IOpenDocumentCommand>(command =>
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
    [ToolAlias("close")]
    public void Close(string fileResource, bool forceClose = false)
    {
        _commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceClose = forceClose;
        });
    }
}
