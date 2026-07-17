namespace Celbridge.Projects;

/// <summary>
/// The reserved host-level property names on a .celbridge instance table: the identity keys
/// (package, contribution) and the display overrides (title, icon, tooltip). Every other key on
/// the table is instance configuration. The parser reads these keys, and the manifest loader
/// forbids a config descriptor from colliding with them, so both sides resolve "which keys are
/// reserved" through this one set.
/// </summary>
public static class InstancePropertyKeys
{
    public const string Package = "package";
    public const string Contribution = "contribution";
    public const string Title = "title";
    public const string Icon = "icon";
    public const string Tooltip = "tooltip";

    /// <summary>
    /// All reserved instance property names, matched with ordinal (case-sensitive) comparison.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Package,
        Contribution,
        Title,
        Icon,
        Tooltip,
    };
}
