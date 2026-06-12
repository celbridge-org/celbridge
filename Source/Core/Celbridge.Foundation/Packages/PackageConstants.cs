namespace Celbridge.Packages;

/// <summary>
/// Constants shared across the package system: well-known file and folder
/// names, the reserved name prefix, and validation limits.
/// </summary>
public static class PackageConstants
{
    /// <summary>
    /// File name of the package manifest at the root of every package folder.
    /// </summary>
    public const string ManifestFileName = "package.toml";

    /// <summary>
    /// Default install folder for packages, relative to the project root.
    /// This is a convention, not a constraint: packages may install elsewhere.
    /// </summary>
    public const string DefaultPackagesFolder = "packages";

    /// <summary>
    /// File name of the generated version history written beside the manifest
    /// when a package is installed. Excluded (case-insensitively) on publish.
    /// </summary>
    public const string HistoryFileName = "HISTORY.md";

    /// <summary>
    /// Name prefix reserved for first-party packages shipped inside Celbridge
    /// module DLLs. Project packages may not claim names under this prefix.
    /// </summary>
    public const string ReservedNamePrefix = "celbridge.";

    /// <summary>
    /// Maximum length of a package name, enforced by the PackageName validator.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Maximum length of the change summary accompanying a published version.
    /// Over-long summaries are rejected, never truncated.
    /// </summary>
    public const int MaxSummaryLength = 512;
}
