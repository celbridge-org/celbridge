namespace Celbridge.Broker;

/// <summary>
/// Marks a static method as a broker tool that is automatically discovered
/// and exposed to all connected clients (Python, JavaScript, AI agents).
/// Tool names use slash-separated paths for natural categorisation
/// (e.g. "document/open", "resource/delete").
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class McpToolAttribute : Attribute
{
    /// <summary>
    /// The slash-separated tool name (e.g. "document/open").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// A human-readable description of what the tool does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public McpToolAttribute(string name)
    {
        Name = name;
    }
}
