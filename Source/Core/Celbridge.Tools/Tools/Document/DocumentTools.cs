using System.Text.Json;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for managing the document editor: open, close, activate, and query open tabs.
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
}
