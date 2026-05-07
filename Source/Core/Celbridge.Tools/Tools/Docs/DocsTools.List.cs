using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class DocsListEntry(string Name, string Kind, string Description);

public record class DocsListResult(IReadOnlyList<DocsListEntry> Docs);

public partial class DocsTools
{
    // Bootstrap tool. Keep summary rich and do not trim.
    /// <summary>
    /// Lists Celbridge's built-in agent guide library — tool usage,
    /// conventions, and gotchas. This is meta-documentation about Celbridge
    /// itself, distinct from any markdown files in the project tree. Returns
    /// conceptual docs first (e.g. resource_keys, regex_syntax,
    /// python_proxy_conventions) then per-tool docs holding extended
    /// rationale for specific MCP tools. Each entry carries a kind and a
    /// one-sentence description; pass any name to docs_read to fetch full
    /// content.
    /// </summary>
    /// <returns>JSON object with field: docs (array of {name, kind, description}); conceptual docs come first, ordered by priority then name; per-tool docs follow, ordered alphabetically.</returns>
    [McpServerTool(Name = "docs_list", ReadOnly = true, Idempotent = true)]
    [ToolAlias("docs.list")]
    public partial CallToolResult List()
    {
        var index = DocLibrary.Index;
        var entries = new List<DocsListEntry>(index.Count);
        foreach (var doc in index)
        {
            entries.Add(new DocsListEntry(doc.Name, doc.Kind.ToString().ToLowerInvariant(), doc.Description));
        }

        var result = new DocsListResult(entries);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolSuccess(json);
    }
}
