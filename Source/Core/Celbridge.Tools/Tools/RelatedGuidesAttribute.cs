namespace Celbridge.Tools;

/// <summary>
/// Declares the supplemental guides that auto-attach the first time this MCP
/// tool method is invoked in a session. Lists concept guides, troubleshooter
/// guides, sibling per-tool guides, or namespace guides whose content the
/// agent should have alongside the per-tool guide. The tool's own per-tool
/// guide and namespace guide are implicit and never appear here. The attribute
/// is mandatory on every MCP tool method even when no extra guides apply
/// (declare it with no arguments) so the choice is always explicit.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RelatedGuidesAttribute : Attribute
{
    /// <summary>
    /// The names of the related guides, in the order they should attach.
    /// </summary>
    public IReadOnlyList<string> Names { get; }

    public RelatedGuidesAttribute(params string[] names)
    {
        Names = names;
    }
}
