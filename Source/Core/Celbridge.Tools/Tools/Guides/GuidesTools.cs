using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for browsing the agent guide library: conceptual guides
/// covering cross-cutting Celbridge topics, plus per-tool guides holding the
/// rich rationale and examples that don't fit in tools/list summaries.
/// </summary>
[McpServerToolType]
public partial class GuidesTools : AgentToolBase
{
    private IGuides? _guides;

    public GuidesTools(IApplicationServiceProvider services) : base(services) { }

    private IGuides Guides => _guides ??= GetRequiredService<IGuides>();
}
