namespace Celbridge.Dependencies;

/// <summary>
/// The algorithm of an archive's expected hash.
/// </summary>
public enum DependencyHashAlgorithm
{
    /// <summary>
    /// SHA-256, a hash of 64 hex digits.
    /// </summary>
    Sha256,

    /// <summary>
    /// SHA-512, a hash of 128 hex digits.
    /// </summary>
    Sha512
}

/// <summary>
/// An archive's expected hash: the algorithm, and the hash as hex.
/// </summary>
public record DependencyHash(DependencyHashAlgorithm Algorithm, string Value);

/// <summary>
/// An archive that installs a dependency: the archive's address, the archive's expected hash, and the folder to
/// install into. ArchiveFolder names a folder inside the archive, which becomes the installed folder. ArchiveFolder
/// is empty when the archive's root becomes the installed folder.
/// </summary>
public record DependencyDownload(string Url, DependencyHash Hash, string FolderPath)
{
    public string ArchiveFolder { get; init; } = string.Empty;
}

/// <summary>
/// Downloads and extracts the archives that install dependencies. Extracts .zip, .tar.gz and .tgz archives.
/// </summary>
public interface IDependencyDownloader
{
    /// <summary>
    /// Downloads the archive, checks the archive's hash and extracts the archive into the download's folder. The
    /// folder appears only once complete. Fails if the hash differs from the expected hash.
    /// </summary>
    Task<Result> DownloadAsync(DependencyDownload download);
}
