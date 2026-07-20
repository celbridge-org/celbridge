using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// The resolved, single activation of an editor contribution: one per active contribution, plus the
/// built-in editors wrapped in the same record under their host-assigned ids. Presentation (title,
/// icon, tooltip) comes from the contribution manifest, not from the project.
/// </summary>
public record EditorInstance
{
    /// <summary>
    /// The id that addresses this editor: the "{package}.{contribution}" contribution reference for a
    /// discovered contribution, or the host-assigned dotted id for a built-in editor.
    /// </summary>
    public EditorInstanceId InstanceId { get; init; }

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
