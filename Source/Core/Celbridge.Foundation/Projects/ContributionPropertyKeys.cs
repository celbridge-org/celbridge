namespace Celbridge.Projects;

/// <summary>
/// The reserved host-level property names on a .celbridge [[contribution]] entry: the identity keys
/// (package, contribution) and the activation flips (disabled, enabled). Every other key on the
/// entry is contribution configuration. The parser reads these keys, and the manifest loader forbids
/// a config descriptor from colliding with them, so both sides resolve "which keys are reserved"
/// through this one set.
/// </summary>
public static class ContributionPropertyKeys
{
    public const string Package = "package";
    public const string Contribution = "contribution";
    public const string Disabled = "disabled";
    public const string Enabled = "enabled";

    /// <summary>
    /// All reserved contribution-entry property names, matched with ordinal (case-sensitive) comparison.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Package,
        Contribution,
        Disabled,
        Enabled,
    };
}
