using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class QueryTools
{
    /// <summary>
    /// Returns context information for AI agents including resource key conventions and project structure.
    /// </summary>
    /// <returns>A Markdown document describing resource key conventions, project structure, and available tools.</returns>
    [McpServerTool(Name = "query_get_context", ReadOnly = true, Idempotent = true)]
    [ToolAlias("query.get_context")]
    public partial CallToolResult GetContext()
    {
        return SuccessResult(LoadEmbeddedResource("Celbridge.Tools.Assets.AgentContext.md"));
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(QueryTools).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return $"Resource '{resourceName}' not found.";
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
