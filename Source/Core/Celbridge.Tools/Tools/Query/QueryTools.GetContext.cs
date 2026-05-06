using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class QueryTools
{
    /// <summary>
    /// Returns context information for AI agents including resource key conventions and project structure.
    /// </summary>
    /// <returns>A Markdown document describing resource key conventions, project structure, and available tools.</returns>
    [McpServerTool(Name = "query_get_context", ReadOnly = true, Idempotent = true)]
    [ToolAlias("query.get_context")]
    public partial CallToolResult GetContext()
    {
        return ToolSuccess(LoadEmbeddedResource("Celbridge.Tools.Assets.AgentContext.md"));
    }
}
