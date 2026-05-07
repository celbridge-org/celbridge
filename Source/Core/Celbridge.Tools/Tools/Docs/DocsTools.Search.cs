using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class DocsSearchMatchEntry(string Name, string Kind, string Description, string Snippet);

public record class DocsSearchResult(
    IReadOnlyList<DocsSearchMatchEntry> Matches,
    int TotalMatches,
    string? Error);

public partial class DocsTools
{
    private const int DocsSearchDefaultLimit = 10;
    private const int DocsSearchMaxLimit = 25;

    // Bootstrap tool. Keep summary rich and do not trim.
    /// <summary>
    /// Searches Celbridge's built-in agent guide library by regex pattern
    /// (meta-documentation about Celbridge — does not search project files;
    /// for that, use file_grep). Matches frontmatter (name, description) and
    /// body content; results are ranked by relevance with snippets of the
    /// strongest match. Use this when you know what you want but not the
    /// exact doc name. Plain words work as patterns; pass regex syntax
    /// (anchors, alternation) to refine. Then call docs_read with the names
    /// you want full content for.
    /// </summary>
    /// <param name="pattern">Regex pattern (.NET flavour, case-insensitive). A literal substring is a valid pattern.</param>
    /// <param name="limit">Maximum matches to return; default 10, capped at 25. Values above the cap are silently clamped.</param>
    /// <returns>JSON object with fields: matches (array of {name, kind, description, snippet}), totalMatches (full match count regardless of limit), error (regex compile error message when present).</returns>
    [McpServerTool(Name = "docs_search", ReadOnly = true, Idempotent = true)]
    [ToolAlias("docs.search")]
    public partial CallToolResult Search(string pattern, int limit = DocsSearchDefaultLimit)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return ToolError("docs_search requires a non-empty 'pattern'.");
        }

        var effectiveLimit = limit <= 0 ? DocsSearchDefaultLimit : Math.Min(limit, DocsSearchMaxLimit);

        var matches = DocLibrary.Search(pattern, out var error);
        var truncated = matches.Take(effectiveLimit).ToList();

        var entries = new List<DocsSearchMatchEntry>(truncated.Count);
        foreach (var match in truncated)
        {
            entries.Add(new DocsSearchMatchEntry(match.Name, match.Kind.ToString().ToLowerInvariant(), match.Description, match.Snippet));
        }

        var result = new DocsSearchResult(entries, matches.Count, error);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolSuccess(json);
    }
}
