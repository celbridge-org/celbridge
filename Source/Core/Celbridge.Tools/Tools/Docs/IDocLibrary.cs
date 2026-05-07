namespace Celbridge.Tools;

/// <summary>
/// Kind of a doc entry. Inferred from the embedded resource path:
/// files under Docs/Concepts/ are concept, files under Docs/Tools/ are tool.
/// </summary>
internal enum DocKind
{
    Concept,
    Tool
}

/// <summary>
/// A single loaded doc, with parsed frontmatter and the original body text.
/// Per-tool docs additionally carry the cached language-specific invocation
/// strings produced from MCP tool reflection.
/// </summary>
internal record class DocEntry(
    string Name,
    DocKind Kind,
    string Description,
    int Priority,
    string Body,
    string? PythonInvocation,
    string? JavaScriptInvocation);

/// <summary>
/// One match returned by IDocLibrary.Search.
/// </summary>
internal record class DocSearchMatch(string Name, DocKind Kind, string Description, string Snippet, double Score);

/// <summary>
/// In-memory doc library backing the docs_* MCP tools. Loaded once at app
/// startup from embedded markdown resources under Celbridge.Tools.Docs.*; the
/// loader validates frontmatter, name uniqueness, and the requirement that
/// every per-tool doc match a registered MCP tool alias. After construction
/// the library is immutable for the life of the process — every method is an
/// O(1) dictionary lookup or a linear regex scan over the cached bodies.
/// </summary>
internal interface IDocLibrary
{
    /// <summary>
    /// All loaded doc entries in the canonical order returned by docs_list:
    /// concepts before per-tool docs, concepts ordered by priority then name,
    /// per-tool docs ordered by name. Computed once at construction.
    /// </summary>
    IReadOnlyList<DocEntry> Index { get; }

    /// <summary>
    /// Returns the doc with the given name, or null when no such doc exists.
    /// Names are exact-match against the frontmatter name field.
    /// </summary>
    DocEntry? GetByName(string name);

    /// <summary>
    /// Returns the cached Python and JavaScript invocation strings for the
    /// MCP tool with the given alias name (e.g. "file_grep"), or null when no
    /// such tool exists.
    /// </summary>
    (string PythonInvocation, string JavaScriptInvocation)? GetToolInvocations(string toolAliasName);

    /// <summary>
    /// True when the given alias name matches a registered MCP tool. Used by
    /// docs_read to decide whether an unknown name should resolve to a stub
    /// tool entry rather than the unknown array.
    /// </summary>
    bool IsKnownToolAliasName(string toolAliasName);

    /// <summary>
    /// Runs the supplied regex pattern against every loaded doc and returns
    /// matches ranked by score (descending), with snippets. On a regex
    /// compile error or pathological-input timeout, sets errorMessage and
    /// returns an empty list.
    /// </summary>
    IReadOnlyList<DocSearchMatch> Search(string pattern, out string? errorMessage);
}
