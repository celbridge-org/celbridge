namespace Celbridge.Broker;

/// <summary>
/// Provides metadata for a parameter on an [McpTool] method.
/// The description is used for auto-generated help and tool schema.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public class McpParamAttribute : Attribute
{
    /// <summary>
    /// A human-readable description of the parameter.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
