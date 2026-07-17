using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// A named, configured use of an editor contribution. Instances are derived one-to-one from the
/// discovered contributions: each contribution yields one instance whose id is the composed
/// "{package}.{contribution}" id, with empty configuration.
/// </summary>
public record EditorInstance
{
    /// <summary>
    /// The instance id: the universal handle addressing this instance in the editor registry,
    /// on the utility rail, in layout persistence, and in the agent tools.
    /// </summary>
    public EditorInstanceId InstanceId { get; init; }

    /// <summary>
    /// The editor contribution this instance instantiates.
    /// </summary>
    public EditorContribution Contribution { get; init; } = new();

    /// <summary>
    /// Configuration keys for this instance. Empty for instances derived from contributions.
    /// </summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } = EmptyConfig;

    private static readonly IReadOnlyDictionary<string, string> EmptyConfig =
        new Dictionary<string, string>();
}
