using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public record class GuidesReadEntry(
    string Name,
    string Kind,
    string Body,
    string? PythonInvocation,
    string? JavaScriptInvocation);

public record class GuidesReadResult(
    IReadOnlyList<GuidesReadEntry> Results,
    IReadOnlyList<string> Unknown);

public partial class GuidesTools
{
    /// <summary>Re-fetch one or more guides after the host context auto-compacted.</summary>
    [McpServerTool(Name = "guides_read", ReadOnly = true, Idempotent = true)]
    [ToolAlias("guides.read")]
    [RelatedGuides]
    public partial CallToolResult Read(string names)
    {
        var parsedNames = ParseNamesArgument(names);
        if (parsedNames.IsFailure)
        {
            return ToolResponse.Error(parsedNames);
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
        return ToolResponse.Success(json);
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
            Body: guide.Body,
            PythonInvocation: guide.PythonInvocation,
            JavaScriptInvocation: guide.JavaScriptInvocation);
    }
}
