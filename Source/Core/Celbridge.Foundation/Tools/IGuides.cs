namespace Celbridge.Tools;

/// <summary>
/// Kind of a guide entry. Inferred from the embedded resource path:
/// files under Guides/Concepts/ are concept, files under Guides/Namespaces/
/// are namespace, files under Guides/Tools/ are tool, files under
/// Guides/Troubleshooters/ are troubleshooter.
/// </summary>
public enum GuideKind
{
    Concept,
    Namespace,
    Tool,
    Troubleshooter
}

/// <summary>
/// A single loaded guide. Per-tool guides additionally carry the cached
/// language-specific invocation strings produced from MCP tool reflection.
/// </summary>
public record class GuideEntry(
    string Name,
    GuideKind Kind,
    string Body,
    string? PythonInvocation,
    string? JavaScriptInvocation);

/// <summary>
/// In-memory guide library backing the guides_read MCP tool and the
/// auto-attach response filter.
/// </summary>
public interface IGuides
{
    /// <summary>
    /// Returns the guide with the given name, or null when no such guide exists.
    /// Names are exact-match against the filename stem.
    /// </summary>
    GuideEntry? GetByName(string name);

    /// <summary>
    /// Returns the [RelatedGuides] names declared on the tool with the given
    /// MCP alias, or an empty list when the tool is unregistered or declares none.
    /// </summary>
    IReadOnlyList<string> GetRelatedGuides(string toolAliasName);
}
