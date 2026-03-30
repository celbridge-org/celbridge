using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for explorer panel operations: structural changes and navigation.
/// </summary>
[McpServerToolType]
public partial class ExplorerTools : AgentToolBase
{
    public ExplorerTools(IApplicationServiceProvider services) : base(services) { }
}
