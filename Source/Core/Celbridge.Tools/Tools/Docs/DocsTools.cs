using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for browsing the agent documentation library: conceptual docs
/// covering cross-cutting Celbridge topics, plus per-tool docs holding the
/// rich rationale and examples that don't fit in tools/list summaries.
/// </summary>
[McpServerToolType]
public partial class DocsTools : AgentToolBase
{
    private IDocLibrary? _docLibrary;

    public DocsTools(IApplicationServiceProvider services) : base(services) { }

    private IDocLibrary DocLibrary => _docLibrary ??= GetRequiredService<IDocLibrary>();
}
