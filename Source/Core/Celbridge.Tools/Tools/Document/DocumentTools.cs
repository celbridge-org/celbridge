using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for document content editing and editor management.
/// </summary>
[McpServerToolType]
public partial class DocumentTools : AgentToolBase
{
    public DocumentTools(IApplicationServiceProvider services) : base(services) { }

    private static List<string> ParseResourceKeys(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith('['))
        {
            var keys = JsonSerializer.Deserialize<List<string>>(trimmed);
            return keys ?? new List<string> { input };
        }

        return new List<string> { input };
    }

    private static Result<List<TextEdit>> ParseEditsJson(string editsJson)
    {
        var edits = new List<TextEdit>();
        var jsonDocument = JsonDocument.Parse(editsJson);

        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail("Edits JSON must be an array of edit objects");
        }

        int index = 0;
        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("line", out var lineElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'line'");
            }

            var column = element.TryGetProperty("column", out var columnElement) ? columnElement.GetInt32() : 1;

            if (!element.TryGetProperty("endLine", out var endLineElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'endLine'");
            }

            var endColumn = element.TryGetProperty("endColumn", out var endColumnElement) ? endColumnElement.GetInt32() : -1;

            if (!element.TryGetProperty("newText", out var newTextElement))
            {
                return Result.Fail($"Edit at index {index}: missing required property 'newText'");
            }

            var line = lineElement.GetInt32();
            var endLine = endLineElement.GetInt32();
            var newText = newTextElement.GetString() ?? string.Empty;

            edits.Add(new TextEdit(line, column, endLine, endColumn, newText));
            index++;
        }

        return edits;
    }
}
