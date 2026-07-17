using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// A named, configured use of an editor contribution. Declared instances come from the project
/// config; built-in editors are wrapped in the same record under their host-assigned ids.
/// </summary>
public record EditorInstance
{
    /// <summary>
    /// The id that addresses this instance: the project-declared table name for declared
    /// instances, or the host-assigned dotted id for a built-in editor.
    /// </summary>
    public EditorInstanceId InstanceId { get; init; }

    /// <summary>
    /// The editor contribution this instance instantiates.
    /// </summary>
    public EditorContribution Contribution { get; init; } = new();

    /// <summary>
    /// Effective configuration for this instance: the contribution's descriptor defaults merged
    /// with the instance's project-config keys, in the Options channel string encoding.
    /// </summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } = EmptyConfig;

    /// <summary>
    /// Literal display title from the instance declaration, or null to use the contribution's
    /// localized display name.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Icon name from the instance declaration, or null to use the contribution's manifest icon.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Literal tooltip from the instance declaration, or null to use the contribution's localized
    /// tooltip.
    /// </summary>
    public string? Tooltip { get; init; }

    private static readonly IReadOnlyDictionary<string, string> EmptyConfig =
        new Dictionary<string, string>();
}
