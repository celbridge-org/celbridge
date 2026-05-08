namespace Celbridge.Tools;

/// <summary>
/// Kind of a guide entry. Inferred from the embedded resource path:
/// files under Guides/Concepts/ are concept, files under Guides/Namespaces/ are
/// namespace, files under Guides/Tools/ are tool. Declaration order is also
/// the canonical sort order for guides_list (Concepts first, then Namespaces,
/// then Tools), since enums sort by underlying integer.
/// </summary>
internal enum GuideKind
{
    Concept,
    Namespace,
    Tool
}

/// <summary>
/// A single loaded guide, with parsed frontmatter and the original body text.
/// Per-tool guides additionally carry the cached language-specific invocation
/// strings produced from MCP tool reflection.
/// </summary>
internal record class GuideEntry(
    string Name,
    GuideKind Kind,
    string Description,
    int Priority,
    string Body,
    string? PythonInvocation,
    string? JavaScriptInvocation);

/// <summary>
/// One match returned by IGuides.Search.
/// </summary>
internal record class GuideSearchMatch(string Name, GuideKind Kind, string Description, string Snippet, double Score);

/// <summary>
/// In-memory guide library backing the guides_* MCP tools. Loaded once at app
/// startup from embedded markdown resources under Celbridge.Tools.Guides.*; the
/// loader validates frontmatter, name uniqueness, and the requirement that
/// every per-tool guide match a registered MCP tool alias. After construction
/// the library is immutable for the life of the process — every method is an
/// O(1) dictionary lookup or a linear regex scan over the cached bodies.
/// </summary>
internal interface IGuides
{
    /// <summary>
    /// All loaded guide entries in the canonical order returned by guides_list:
    /// concepts before per-tool guides, concepts ordered by priority then name,
    /// per-tool guides ordered by name. Computed once at construction.
    /// </summary>
    IReadOnlyList<GuideEntry> Index { get; }

    /// <summary>
    /// Returns the guide with the given name, or null when no such guide exists.
    /// Names are exact-match against the frontmatter name field.
    /// </summary>
    GuideEntry? GetByName(string name);

    /// <summary>
    /// Returns the cached Python and JavaScript invocation strings for the
    /// MCP tool with the given alias name (e.g. "file_grep"), or null when no
    /// such tool exists.
    /// </summary>
    (string PythonInvocation, string JavaScriptInvocation)? GetToolInvocations(string toolAliasName);

    /// <summary>
    /// True when the given alias name matches a registered MCP tool. Used by
    /// guides_read to decide whether an unknown name should resolve to a stub
    /// tool entry rather than the unknown array.
    /// </summary>
    bool IsKnownToolAliasName(string toolAliasName);

    /// <summary>
    /// Runs the supplied regex pattern against every loaded guide and returns
    /// matches ranked by score (descending), with snippets. On a regex
    /// compile error or pathological-input timeout, sets errorMessage and
    /// returns an empty list.
    /// </summary>
    IReadOnlyList<GuideSearchMatch> Search(string pattern, out string? errorMessage);
}
