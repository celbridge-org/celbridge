using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class GuidesListEntry(string Name, string Kind, string Description);

public record class GuidesListResult(IReadOnlyList<GuidesListEntry> Guides);

public partial class GuidesTools
{
    // Bootstrap tool. Summary stays informative for cold-start use; trim conservatively.
    /// <summary>
    /// Enumerate the built-in agent guide library: each entry's name, kind (concept or tool), and one-line description. Pass any name to guides_read for full content. Meta-documentation about Celbridge — not project files.
    /// </summary>
    /// <returns>JSON: {guides: [{name, kind, description}]} — concepts first, then per-tool guides alphabetically.</returns>
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
        return ToolResponse.Success(json);
    }
}
