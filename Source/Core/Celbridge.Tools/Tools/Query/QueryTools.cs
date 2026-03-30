using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for agent context and knowledge retrieval.
/// </summary>
[McpServerToolType]
public partial class QueryTools : AgentToolBase
{
    public QueryTools(IApplicationServiceProvider services) : base(services) { }
}
