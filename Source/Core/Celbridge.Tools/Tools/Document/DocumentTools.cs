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

    private static List<TextEdit> ParseEditsJson(string editsJson)
    {
        var edits = new List<TextEdit>();
        var jsonDocument = JsonDocument.Parse(editsJson);

        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            var line = element.GetProperty("line").GetInt32();
            var column = element.TryGetProperty("column", out var columnElement) ? columnElement.GetInt32() : 1;
            var endLine = element.GetProperty("endLine").GetInt32();
            var endColumn = element.TryGetProperty("endColumn", out var endColumnElement) ? endColumnElement.GetInt32() : -1;
            var newText = element.GetProperty("newText").GetString() ?? string.Empty;

            edits.Add(new TextEdit(line, column, endLine, endColumn, newText));
        }

        return edits;
    }
}
