namespace Celbridge.Packages;

/// <summary>
/// Discovery origin of a package. Determines whether file reads cross the
/// IResourceFileSystem gateway (Project) or stay on direct File.* IO (Bundled,
/// until the bundled-from-assembly migration lands).
/// </summary>
public enum PackageOrigin
{
    /// <summary>
    /// First-party package shipped inside a Celbridge module DLL. Read sites
    /// stay on direct File.* IO against the install folder.
    /// </summary>
    Bundled,

    /// <summary>
    /// User package discovered under the project's packages/ folder. Read
    /// sites resolve a ResourceKey via IResourceRegistry and read through
    /// IResourceFileSystem so the gateway contract holds for every project-tree
    /// byte.
    /// </summary>
    Project
}

/// <summary>
/// Package identity, permissions, and hosting information.
/// Shared across all contributions from the same package.
/// </summary>
public partial record PackageInfo
{
    /// <summary>
    /// Unique name identifying the package (e.g., "my-widget"). Names that begin
    /// with the "celbridge." prefix are reserved for first-party packages shipped
    /// inside Celbridge module DLLs.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional display name of the package (from the manifest's 'title' key).
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Optional feature flag. When set, all contributions are disabled if this feature is off.
    /// </summary>
    public string? FeatureFlag { get; init; }

    /// <summary>
    /// Tool allowlist declared under [permissions].tools.
    /// </summary>
    public IReadOnlyList<string> PermittedTools { get; init; } = Array.Empty<string>();

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
    /// Whether the package was discovered as a bundled (in-module) or project
    /// (project-tree) package. Drives the read path selection at every site
    /// that loads bytes for the package.
    /// </summary>
    public PackageOrigin Origin { get; init; }

    /// <summary>
    /// The folder containing the package (set during loading, not from TOML).
    /// </summary>
    public string PackageFolder { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this package's assets (set during loading, not from TOML).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}
