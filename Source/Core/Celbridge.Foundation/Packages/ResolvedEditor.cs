using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// What a declared contribution resolves to, holding the contribution and the config it runs with.
/// There is one per active contribution, whatever its type, plus the built-in editors wrapped in the
/// same record under their host-assigned ids. The views that open on top of it are created by the
/// editor's factory: one for a utility, one per open document for a document editor.
/// </summary>
public record ResolvedEditor
{
    /// <summary>
    /// The id that addresses this editor: the "{package}.{contribution}" contribution reference for a
    /// discovered contribution, or the host-assigned dotted id for a built-in editor.
    /// </summary>
    public EditorId EditorId { get; init; }

    /// <summary>
    /// The editor contribution this activates.
    /// </summary>
    public EditorContribution Contribution { get; init; } = new();

    /// <summary>
    /// Effective configuration: the contribution's descriptor defaults merged with the project's
    /// per-contribution config keys, in the Options channel string encoding.
    /// </summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } = EmptyConfig;

    private static readonly IReadOnlyDictionary<string, string> EmptyConfig =
        new Dictionary<string, string>();
}
