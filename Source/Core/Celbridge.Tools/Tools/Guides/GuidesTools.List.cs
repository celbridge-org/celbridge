using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class GuidesListEntry(string Name, string Kind, string Description);

public record class GuidesListResult(IReadOnlyList<GuidesListEntry> Guides);

public partial class GuidesTools
{
    // Bootstrap tool. Keep summary rich and do not trim.
    /// <summary>
    /// Lists Celbridge's built-in agent guide library — tool usage,
    /// conventions, and gotchas. This is meta-documentation about Celbridge
    /// itself, distinct from any markdown files in the project tree. Returns
    /// conceptual guides first (e.g. resource_keys, regex_syntax,
    /// python_proxy_conventions) then per-tool guides holding extended
    /// rationale for specific MCP tools. Each entry carries a kind and a
    /// one-sentence description; pass any name to guides_read to fetch full
    /// content.
    /// </summary>
    /// <returns>JSON object with field: guides (array of {name, kind, description}); conceptual guides come first, ordered by priority then name; per-tool guides follow, ordered alphabetically.</returns>
    [McpServerTool(Name = "guides_list", ReadOnly = true, Idempotent = true)]
    [ToolAlias("guides.list")]
    public partial CallToolResult List()
    {
        var index = Guides.Index;
        var entries = new List<GuidesListEntry>(index.Count);
        foreach (var guide in index)
        {
            entries.Add(new GuidesListEntry(guide.Name, guide.Kind.ToString().ToLowerInvariant(), guide.Description));
        }

        var result = new GuidesListResult(entries);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolSuccess(json);
    }
}
