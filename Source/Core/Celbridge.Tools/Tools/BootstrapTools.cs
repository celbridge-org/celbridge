namespace Celbridge.Tools;

/// <summary>
/// Single source of truth for the bootstrap tool set. Bootstrap tools are
/// the small group whose XML stays rich because they are the agent's only
/// available context before any guide has been read; every other tool is
/// read about in its per-tool guide on demand. They are also exempt from
/// the cold-start gate so the agent can use them to satisfy it. Adding or
/// removing a bootstrap tool only happens here.
/// </summary>
public static class BootstrapTools
{
    /// <summary>
    /// MCP tool names (matching the [McpServerTool(Name = "...")] attribute,
    /// not the dotted alias) of the tools in the bootstrap set.
    /// </summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "guides_list",
        "guides_read",
        "guides_search",
    };

    /// <summary>
    /// True when the given MCP tool name is in the bootstrap set.
    /// </summary>
    public static bool Contains(string toolName)
    {
        return Names.Contains(toolName);
    }
}
