namespace Celbridge.Packages;

/// <summary>
/// Describes a bundled package contributed by a module.
/// Bundled packages use ids under the reserved "celbridge." namespace.
/// </summary>
public sealed record BundledPackageDescriptor
{
    /// <summary>
    /// Absolute path to the folder containing the package's package.toml.
    /// </summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>
    /// Optional override for the WebView virtual host name the package is served under.
    /// When null, the default hostname is derived from the package id.
    /// </summary>
    public string? HostNameOverride { get; init; }

    /// <summary>
    /// Named secrets (e.g. license keys) supplied from the module's compiled source code.
    /// </summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When true, DevTools are permanently disabled on any WebView hosting this package,
    /// regardless of the user's feature flag or build configuration. Intended for bundled
    /// packages that embed sensitive material (e.g. licensed assets, secret keys) that
    /// must not be exposed through browser DevTools. Because this lives on the descriptor
    /// and not the public manifest, third-party packages cannot set it.
    /// </summary>
    public bool DevToolsBlocked { get; init; }

    /// <summary>
    /// When true, this package's WebView is served over the loopback file server (the
    /// /package/{name}/ route) and addressed root-relative, instead of the SetVirtualHostNameToFolderMapping
    /// virtual host. Transitional during the macOS port: editors flip to this as they are migrated, and
    /// the virtual-host path is removed once all are across. Loopback-served editors run on every head;
    /// virtual-host editors are unsupported on the Skia heads where the mapping is a no-op.
    /// </summary>
    public bool ServedViaLoopback { get; init; }
}
