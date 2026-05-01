namespace Celbridge.Tools;

/// <summary>
/// Declares the dot-notation alias under which an MCP tool method is exposed on
/// the Python and JavaScript proxies (e.g. "file.read_binary" surfaces as
/// `cel.file.read_binary` in Python and `cel.file.readBinary` in JS). Apply
/// to every MCP tool method — proxy exposure is the default.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ToolAliasAttribute : Attribute
{
    /// <summary>
    /// The proxy alias for the tool, in dot notation (e.g. "file.read_binary").
    /// </summary>
    public string Alias { get; }

    public ToolAliasAttribute(string alias)
    {
        Alias = alias;
    }
}
