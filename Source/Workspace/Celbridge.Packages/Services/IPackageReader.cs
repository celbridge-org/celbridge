namespace Celbridge.Packages;

/// <summary>
/// Synchronous file-read primitives used by the package layer to load
/// manifests, localization, templates, and extensions lists. The interface
/// keeps PackageManifestLoader and PackageLocalizationService unaware of
/// whether the bytes ultimately come from disk (bundled) or from IFileStorage
/// (project), so each loader can stay on a single sync code path.
/// </summary>
public interface IPackageReader
{
    /// <summary>
    /// True when a file exists at the given absolute path.
    /// </summary>
    bool Exists(string absolutePath);

    /// <summary>
    /// Reads the file at the given absolute path as UTF-8 text.
    /// </summary>
    Result<string> ReadAllText(string absolutePath);

    /// <summary>
    /// Reads the file at the given absolute path as raw bytes.
    /// </summary>
    Result<byte[]> ReadAllBytes(string absolutePath);
}
