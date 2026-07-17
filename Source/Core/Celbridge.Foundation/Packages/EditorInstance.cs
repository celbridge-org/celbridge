using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// A named, configured use of an editor contribution.
/// </summary>
public record EditorInstance
{
    /// <summary>
    /// The id that addresses this instance, composed from its package and contribution ids.
    /// </summary>
    public EditorInstanceId InstanceId { get; init; }

    /// <summary>
    /// The editor contribution this instance instantiates.
    /// </summary>
    public EditorContribution Contribution { get; init; } = new();

    /// <summary>
    /// Configuration keys for this instance.
    /// </summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } = EmptyConfig;

    private static readonly IReadOnlyDictionary<string, string> EmptyConfig =
        new Dictionary<string, string>();
}
