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
    // Bootstrap tool. Keep summary rich and do not trim.
    /// <summary>
    /// Reads one or more entries from Celbridge's built-in agent guide
    /// library (meta-documentation about Celbridge — distinct from project
    /// files). A name can be either a conceptual guide name (e.g.
    /// 'resource_keys') or a tool alias name (e.g. 'file_grep'); the
    /// resolver tries both. Tool entries also carry the Python and JavaScript
    /// invocation strings so the agent doesn't have to translate from the MCP
    /// tool name. Names that resolve to neither land in the unknown array
    /// rather than failing the call.
    /// </summary>
    /// <param name="names">Names of guides or tools to fetch, JSON-encoded as a string array (e.g. '["resource_keys","file_grep"]').</param>
    /// <returns>JSON object with fields: results (array of {name, kind, description, body, pythonInvocation?, javascriptInvocation?}), unknown (array of names that resolved to neither a guide nor a tool).</returns>
    [McpServerTool(Name = "guides_read", ReadOnly = true, Idempotent = true)]
    [ToolAlias("guides.read")]
    public partial CallToolResult Read(string names)
    {
        var parsedNames = ParseNamesArgument(names);
        if (parsedNames.IsFailure)
        {
            return ToolError(parsedNames);
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
        if (guide is not null)
        {
            return new GuidesReadEntry(
                Name: guide.Name,
                Kind: guide.Kind.ToString().ToLowerInvariant(),
                Description: guide.Description,
                Body: guide.Body,
                PythonInvocation: guide.PythonInvocation,
                JavaScriptInvocation: guide.JavaScriptInvocation);
        }

        if (library.IsKnownToolAliasName(requestedName))
        {
            var invocations = library.GetToolInvocations(requestedName);
            var stubBody = $"No extended documentation for `{requestedName}`. See its summary in tools/list for the call shape.";
            return new GuidesReadEntry(
                Name: requestedName,
                Kind: GuideKind.Tool.ToString().ToLowerInvariant(),
                Description: stubBody,
                Body: stubBody,
                PythonInvocation: invocations?.PythonInvocation,
                JavaScriptInvocation: invocations?.JavaScriptInvocation);
        }

        return null;
    }
}
