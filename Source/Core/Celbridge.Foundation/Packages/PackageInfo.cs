namespace Celbridge.Packages;

/// <summary>
/// Package identity, permissions, and hosting information.
/// Shared across all contributions from the same package.
/// </summary>
public partial record PackageInfo
{
    /// <summary>
    /// Unique identifier for the package (e.g., "celbridge.notes").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the package (from package.toml).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional feature flag. When set, all contributions are disabled if this feature is off.
    /// </summary>
    public string? FeatureFlag { get; init; }

    /// <summary>
    /// The folder containing the package (set during loading, not from TOML).
    /// </summary>
    public string PackageFolder { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this package's assets (set during loading, not from TOML).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}
