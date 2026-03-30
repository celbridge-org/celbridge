namespace Celbridge.Tools;

/// <summary>
/// Declares the Python-friendly alias for an MCP tool method.
/// This is mandatory on every [McpServerTool] method. The system
/// fails to initialize if any tool is missing this declaration.
/// Supports dot notation for namespacing (e.g. "sheet.delete").
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ToolAliasAttribute : Attribute
{
    /// <summary>
    /// The short alias used as the Python method name (e.g. "open", "delete", "sheet.delete").
    /// </summary>
    public string Alias { get; }

    public ToolAliasAttribute(string alias)
    {
        Alias = alias;
    }
}
