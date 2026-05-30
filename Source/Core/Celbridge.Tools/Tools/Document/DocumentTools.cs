using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for managing the document editor: open, close, activate, and query open tabs.
/// </summary>
[McpServerToolType]
public partial class DocumentTools : AgentToolBase
{
    public DocumentTools(IApplicationServiceProvider services) : base(services) { }

    // Accepts a single resource key string OR a JSON array of resource keys.
    // The leading '[' is what disambiguates the two modes; anything else is
    // treated as a single key. JSON parsing failures (malformed array) come
    // back as a friendly Result.Fail rather than an uncaught JsonException.
    private static Result<List<string>> ParseResourceKeys(string input)
    {
        var trimmed = input.Trim();
        if (!trimmed.StartsWith('['))
        {
            return new List<string> { input };
        }

        return ParseJsonArgument<List<string>>(trimmed, "fileResource");
    }
}
