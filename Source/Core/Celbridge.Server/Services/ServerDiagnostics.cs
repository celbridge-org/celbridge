using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Celbridge.Projects;
using Path = System.IO.Path;

namespace Celbridge.Server.Services;

/// <summary>
/// Home for broker-side diagnostic operations such as the tools/list
/// payload-size report. Reads raw payload data through IMcpToolBridge so
/// the broker's HTTP and session handling stays in one place. New
/// diagnostic methods that need broker introspection or report-writing
/// belong here so they share one set of dependencies rather than being
/// scattered across the broker types.
/// </summary>
public class ServerDiagnostics
{
    // The chars/4 rule is Anthropic's published rule of thumb. Not Claude's
    // actual tokeniser, but consistent across runs so the trim-trend
    // comparison the report drives is meaningful.
    private const string TokenisationLabel = "approximate (chars/4)";

    private readonly IMcpToolBridge _toolBridge;
    private readonly IProjectService _projectService;

    public ServerDiagnostics(IMcpToolBridge toolBridge, IProjectService projectService)
    {
        _toolBridge = toolBridge;
        _projectService = projectService;
    }

    public async Task<string> GeneratePayloadReportAsync()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            throw new InvalidOperationException("Cannot write payload report because no project is loaded.");
        }

        var rawJson = await _toolBridge.GetRawToolsListJsonAsync();
        if (string.IsNullOrEmpty(rawJson))
        {
            throw new InvalidOperationException("MCP server did not return a tools/list payload.");
        }

        var toolsArray = JsonNode.Parse(rawJson) as JsonArray;
        if (toolsArray is null)
        {
            throw new InvalidOperationException("tools/list payload was not a JSON array.");
        }

        var entries = new List<ToolEntry>(toolsArray.Count);
        foreach (var toolNode in toolsArray)
        {
            if (toolNode is not JsonObject toolObject)
            {
                continue;
            }

            var toolName = toolObject["name"]?.GetValue<string>() ?? string.Empty;
            var characters = toolObject.ToJsonString().Length;
            var tokens = ApproximateTokenCount(characters);
            entries.Add(new ToolEntry(toolName, ExtractNamespace(toolName), characters, tokens));
        }

        var totalCharacters = entries.Sum(entry => entry.Characters);
        var totalTokens = entries.Sum(entry => entry.Tokens);

        var sortedEntries = entries
            .OrderByDescending(entry => entry.Tokens)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        var sortedNamespaces = AggregateByNamespace(entries)
            .OrderByDescending(entry => entry.Tokens)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        var timestamp = DateTime.Now;
        var fileName = $"payload-report-{timestamp:yyyyMMdd-HHmmss}.md";
        var filePath = Path.Combine(currentProject.ProjectFolderPath, fileName);

        var markdown = BuildMarkdownReport(sortedEntries, sortedNamespaces, totalCharacters, totalTokens, timestamp);
        await File.WriteAllTextAsync(filePath, markdown);

        return filePath;
    }

    private static int ApproximateTokenCount(int characterCount)
    {
        return (characterCount + 3) / 4;
    }

    // Tool names follow the namespace_method convention (e.g. "app_get_status").
    // Everything before the first underscore is the namespace; tools without an
    // underscore are bucketed under their full name.
    private static string ExtractNamespace(string toolName)
    {
        var underscoreIndex = toolName.IndexOf('_');
        if (underscoreIndex <= 0)
        {
            return toolName;
        }

        return toolName[..underscoreIndex];
    }

    private static List<NamespaceEntry> AggregateByNamespace(IReadOnlyList<ToolEntry> entries)
    {
        var aggregates = new Dictionary<string, (int ToolCount, int Characters, int Tokens)>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            aggregates.TryGetValue(entry.Namespace, out var current);
            aggregates[entry.Namespace] = (current.ToolCount + 1, current.Characters + entry.Characters, current.Tokens + entry.Tokens);
        }

        var result = new List<NamespaceEntry>(aggregates.Count);
        foreach (var pair in aggregates)
        {
            result.Add(new NamespaceEntry(pair.Key, pair.Value.ToolCount, pair.Value.Characters, pair.Value.Tokens));
        }

        return result;
    }

    private static string BuildMarkdownReport(
        IReadOnlyList<ToolEntry> sortedEntries,
        IReadOnlyList<NamespaceEntry> sortedNamespaces,
        int totalCharacters,
        int totalTokens,
        DateTime timestamp)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Tools/list payload report");
        builder.AppendLine();
        builder.Append("- Generated: ").AppendLine(timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        builder.Append("- Total tools: ").AppendLine(sortedEntries.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("- Total characters: ").AppendLine(totalCharacters.ToString(CultureInfo.InvariantCulture));
        builder.Append("- Total tokens (approx): ").AppendLine(totalTokens.ToString(CultureInfo.InvariantCulture));
        builder.Append("- Tokeniser: ").AppendLine(TokenisationLabel);
        builder.AppendLine();

        builder.AppendLine("## Per-namespace totals");
        builder.AppendLine();
        builder.AppendLine("| Namespace | Tools | Characters | Tokens |");
        builder.AppendLine("|---|---:|---:|---:|");
        foreach (var ns in sortedNamespaces)
        {
            builder
                .Append("| ").Append(ns.Name)
                .Append(" | ").Append(ns.ToolCount.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(ns.Characters.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(ns.Tokens.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Per-tool totals (sorted by tokens, descending)");
        builder.AppendLine();
        builder.AppendLine("| Tool | Namespace | Characters | Tokens |");
        builder.AppendLine("|---|---|---:|---:|");
        foreach (var entry in sortedEntries)
        {
            builder
                .Append("| ").Append(entry.Name)
                .Append(" | ").Append(entry.Namespace)
                .Append(" | ").Append(entry.Characters.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(entry.Tokens.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    private record class ToolEntry(string Name, string Namespace, int Characters, int Tokens);

    private record class NamespaceEntry(string Name, int ToolCount, int Characters, int Tokens);
}
