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
    /// Named secrets (e.g. license keys) supplied from the module's compiled source code.
    /// </summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// When true, DevTools are permanently disabled on any WebView hosting this package,
    /// regardless of the user's feature flag or build configuration.
    /// </summary>
    public bool DevToolsBlocked { get; init; }
}
