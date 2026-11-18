namespace Celbridge.Dependencies;

/// <summary>
/// An archive that installs a dependency: the archive's address, the archive's expected SHA-256 hash as hex, and
/// the folder to install into. ArchiveFolder names a folder inside the archive, which becomes the installed folder.
/// ArchiveFolder is empty when the archive's root becomes the installed folder.
/// </summary>
public record DependencyDownload(string Url, string Sha256, string FolderPath)
{
    public string ArchiveFolder { get; init; } = string.Empty;
}

/// <summary>
/// Downloads and extracts the archives that install dependencies. A .zip archive and a .tar.gz archive both
/// extract.
/// </summary>
public interface IDependencyDownloader
{
    /// <summary>
    /// Downloads the archive, checks the archive's hash and extracts the archive into the download's folder. The
    /// folder appears only once complete. Fails if the hash differs from the expected hash.
    /// </summary>
    Task<Result> DownloadAsync(DependencyDownload download);
}
