using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>App state: app version, project load status, feature flags, focused panel, layout.</summary>
    [McpServerTool(Name = "app_get_state", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_state")]
    [RelatedGuides("workspace_panels", "project_structure")]
    public partial CallToolResult GetState()
    {
        var stateProvider = GetRequiredService<IAppStateProvider>();
        var result = stateProvider.GetState();
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
