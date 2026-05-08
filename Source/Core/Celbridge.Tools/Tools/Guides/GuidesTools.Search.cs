using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class GuidesSearchMatchEntry(string Name, string Kind, string Description, string Snippet);

public record class GuidesSearchResult(
    IReadOnlyList<GuidesSearchMatchEntry> Matches,
    int TotalMatches,
    string? Error);

public partial class GuidesTools
{
    private const int GuidesSearchDefaultLimit = 10;
    private const int GuidesSearchMaxLimit = 25;

    // Bootstrap tool. Summary stays informative for cold-start use; trim conservatively.
    /// <summary>
    /// Regex-search the built-in agent guide library; returns ranked matches with snippets. Use when you know roughly what you want but not the exact name, then call guides_read for full bodies. For project file content, use file_grep.
    /// </summary>
    /// <param name="pattern">.NET regex, case-insensitive. Plain substrings are valid patterns.</param>
    /// <param name="limit">Max matches; default 10, capped at 25.</param>
    /// <returns>JSON: {matches: [{name, kind, description, snippet}], totalMatches: int, error: string?}.</returns>
    [McpServerTool(Name = "guides_search", ReadOnly = true, Idempotent = true)]
    [ToolAlias("guides.search")]
    public partial CallToolResult Search(string pattern, int limit = GuidesSearchDefaultLimit)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return ToolError("guides_search requires a non-empty 'pattern'.");
        }

        var effectiveLimit = limit <= 0 ? GuidesSearchDefaultLimit : Math.Min(limit, GuidesSearchMaxLimit);

        var matches = Guides.Search(pattern, out var error);
        var truncated = matches.Take(effectiveLimit).ToList();

        var entries = new List<GuidesSearchMatchEntry>(truncated.Count);
        foreach (var match in truncated)
        {
            entries.Add(new GuidesSearchMatchEntry(match.Name, match.Kind.ToString().ToLowerInvariant(), match.Description, match.Snippet));
        }

        var result = new GuidesSearchResult(entries, matches.Count, error);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolSuccess(json);
    }
}
