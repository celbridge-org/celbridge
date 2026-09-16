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
    /// Extension carried by a per-contribution editor manifest, which is always named with a stem
    /// ("code.editor.toml"). A file named "editor.toml" alone is not a manifest.
    /// </summary>
    public const string EditorManifestExtension = ".editor.toml";

    /// <summary>
    /// Default install folder for packages, relative to the project root.
    /// </summary>
    public const string DefaultPackagesFolder = "packages";

    /// <summary>
    /// Name prefix reserved for first-party packages shipped inside Celbridge module DLLs.
    /// </summary>
    public const string ReservedNamePrefix = "celbridge-";

    /// <summary>
    /// Maximum length of a package name.
    /// </summary>
    public const int MaxNameLength = 64;
}
