namespace Celbridge.Packages;

/// <summary>
/// Well-known names and validation limits shared across the package system.
/// </summary>
public static class PackageConstants
{
    /// <summary>
    /// File name of the package manifest at the root of a package folder.
    /// </summary>
    public const string ManifestFileName = "package.toml";

    /// <summary>
    /// Default install folder for packages, relative to the project root.
    /// </summary>
    public const string DefaultPackagesFolder = "packages";

    /// <summary>
    /// File name of the generated version-history changelog beside a package's manifest.
    /// </summary>
    public const string HistoryFileName = "HISTORY.md";

    /// <summary>
    /// Name prefix reserved for first-party packages shipped inside Celbridge module DLLs.
    /// </summary>
    public const string ReservedNamePrefix = "celbridge.";

    /// <summary>
    /// Name of the server-managed alias that points at a package's highest live version.
    /// </summary>
    public const string LatestAlias = "latest";

    /// <summary>
    /// Maximum length of a package name.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Maximum length of the change summary accompanying a published version.
    /// </summary>
    public const int MaxSummaryLength = 512;
}
