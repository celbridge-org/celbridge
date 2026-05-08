using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class GuidesReadEntry(
    string Name,
    string Kind,
    string Description,
    string Body,
    string? PythonInvocation,
    string? JavaScriptInvocation);

public record class GuidesReadResult(
    IReadOnlyList<GuidesReadEntry> Results,
    IReadOnlyList<string> Unknown);

public partial class GuidesTools
{
    // Bootstrap tool. Summary stays informative for cold-start use; trim conservatively.
    /// <summary>
    /// Read one or more guides from the built-in library by name. A name resolves to either a concept guide (e.g. 'resource_keys') or a tool name (e.g. 'file_grep'). Pair with guides_list to enumerate, guides_search for regex lookup. Names that match neither land in 'unknown' rather than failing.
    /// </summary>
    /// <param name="names">JSON array of names, e.g. '["resource_keys","file_grep"]'.</param>
    /// <returns>JSON: {results: [{name, kind, description, body, pythonInvocation?, javascriptInvocation?}], unknown: [string]}. Tool entries carry Python and JavaScript invocation strings.</returns>
    [McpServerTool(Name = "guides_read", ReadOnly = true, Idempotent = true)]
    [ToolAlias("guides.read")]
    public partial CallToolResult Read(string names)
    {
        var parsedNames = ParseNamesArgument(names);
        if (parsedNames.IsFailure)
        {
            return BootstrapToolError(parsedNames.MessageChain);
        }

        var library = Guides;
        var results = new List<GuidesReadEntry>();
        var unknown = new List<string>();

        foreach (var requestedName in parsedNames.Value)
        {
            var entry = ResolveEntry(library, requestedName);
            if (entry is null)
            {
                unknown.Add(requestedName);
                continue;
            }
            results.Add(entry);
        }

        var payload = new GuidesReadResult(results, unknown);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return ToolSuccess(json);
    }

    private static Result<List<string>> ParseNamesArgument(string names)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            return Result.Fail("guides_read requires a non-empty 'names' JSON array.");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(names);
            if (parsed is null || parsed.Count == 0)
            {
                return Result.Fail("guides_read requires a non-empty 'names' JSON array.");
            }
            return parsed;
        }
        catch (JsonException exception)
        {
            return Result.Fail($"guides_read 'names' must be a JSON array of strings: {exception.Message}");
        }
    }

    private static GuidesReadEntry? ResolveEntry(IGuides library, string requestedName)
    {
        var guide = library.GetByName(requestedName);
        if (guide is null)
        {
            return null;
        }

        return new GuidesReadEntry(
            Name: guide.Name,
            Kind: guide.Kind.ToString().ToLowerInvariant(),
            Description: guide.Description,
            Body: guide.Body,
            PythonInvocation: guide.PythonInvocation,
            JavaScriptInvocation: guide.JavaScriptInvocation);
    }
}
