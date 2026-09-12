namespace Celbridge.Tools;

/// <summary>
/// Marks an MCP tool that reaches the workshop server. Tools carrying this are withheld from
/// tools/list and refused at tools/call in a build where the workshop feature flag is off, so a
/// build that did not opt in never offers them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WorkshopToolAttribute : Attribute
{
}
