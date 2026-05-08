using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>Bring an already-open document to the foreground (make it the active tab).</summary>
    [McpServerTool(Name = "document_activate", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.activate")]
    public async partial Task<CallToolResult> Activate(string fileResource)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.Error($"Invalid resource key: '{fileResource}'");
        }

        var activateResult = await ExecuteCommandAsync<IActivateDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
        });
        if (activateResult.IsFailure)
        {
            return ToolResponse.Error(activateResult);
        }

        return ToolResponse.Success("ok");
    }
}
