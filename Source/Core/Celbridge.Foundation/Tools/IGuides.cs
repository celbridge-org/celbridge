namespace Celbridge.Tools;

/// <summary>
/// Kind of a guide entry. Inferred from the embedded resource path:
/// files under Guides/Concepts/ are concept, files under Guides/Namespaces/ are
/// namespace, files under Guides/Tools/ are tool.
/// </summary>
public enum GuideKind
{
    Concept,
    Namespace,
    Tool
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
/// auto-attach response filter. Loaded once at app startup from embedded
/// markdown resources under Celbridge.Tools.Guides.*; the loader validates
/// filename uniqueness and the requirement that every per-tool guide match a
/// registered MCP tool alias and every namespace guide match a registered
/// namespace. After construction the library is immutable for the life of
/// the process — every method is an O(1) dictionary lookup.
/// </summary>
public interface IGuides
{
    /// <summary>
    /// Returns the guide with the given name, or null when no such guide exists.
    /// Names are exact-match against the filename stem.
    /// </summary>
    GuideEntry? GetByName(string name);
}
