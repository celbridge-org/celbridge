using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>Document editor state: active document, section layout, all open tabs and their positions.</summary>
    [McpServerTool(Name = "document_get_state", ReadOnly = true)]
    [ToolAlias("document.get_state")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> GetState()
    {
        var stateProvider = GetRequiredService<IDocumentStateProvider>();
        var stateResult = await stateProvider.GetStateAsync();
        if (stateResult.IsFailure)
        {
            return ToolResponse.Error(stateResult);
        }
        var state = stateResult.Value;
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return ToolResponse.Success(json);
    }
}
