namespace Celbridge.Packages;

/// <summary>
/// Package identity, permissions, and hosting information.
/// Shared across all contributions from the same package.
/// </summary>
public partial record PackageInfo
{
    /// <summary>
    /// Unique identifier for the package (e.g., "celbridge.notes"). Ids that begin
    /// with the "celbridge." prefix are reserved for first-party packages shipped
    /// inside Celbridge module DLLs. third-party packages should choose a different
    /// namespace.
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
    /// Tool allowlist declared under [mod].requires_tools.
    /// </summary>
    public IReadOnlyList<string> RequiresTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Named secrets supplied by the module that bundles this package. Populated
    /// only via BundledPackageDescriptor. Always empty for non-bundled packages.
    /// </summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When true, DevTools are permanently disabled on any WebView hosting this
    /// package, regardless of the user's feature flag or build configuration.
    /// Populated only via BundledPackageDescriptor; always false for non-bundled
    /// packages.
    /// </summary>
    public bool DevToolsBlocked { get; init; }

    /// <summary>
    /// The folder containing the package (set during loading, not from TOML).
    /// </summary>
    public string PackageFolder { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this package's assets (set during loading, not from TOML).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}
