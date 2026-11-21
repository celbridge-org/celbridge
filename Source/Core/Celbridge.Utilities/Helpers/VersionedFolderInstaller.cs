using Celbridge.Dependencies;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Installs one version of a dependency into a folder named by the version. The folders of the dependency's versions
/// share one parent folder. Once the version installs, the folders of the other versions are deleted.
/// </summary>
public sealed class VersionedFolderInstaller
{
    private readonly ILocalFileSystem _fileSystem;
    private readonly IDependencyDownloader _downloader;
    private readonly ILogger _logger;

    public VersionedFolderInstaller(ILocalFileSystem fileSystem, IDependencyDownloader downloader, ILogger logger)
    {
        _fileSystem = fileSystem;
        _downloader = downloader;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the archive into the download's folder, which is named by the version. Succeeds at once when the
    /// installed file is already there, since another copy of Celbridge may have installed the version.
    /// </summary>
    public async Task<Result> InstallAsync(DependencyDownload download, string installedFilePath)
    {
        if (await _fileSystem.GetKindAsync(installedFilePath) == StorageItemKind.File)
        {
            return Result.Ok();
        }

        // A folder without the installed file is incomplete. The download renames its extracted folder to the
        // folder's name, so the incomplete folder is deleted first.
        var folderPath = download.FolderPath;
        if (await _fileSystem.GetKindAsync(folderPath) == StorageItemKind.Folder)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(folderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                return Result.Fail($"Failed to delete the incomplete folder '{folderPath}'")
                    .WithErrors(deleteResult);
            }
        }

        var downloadResult = await _downloader.DownloadAsync(download);
        if (downloadResult.IsFailure)
        {
            return Result.Fail($"Failed to download the archive into '{folderPath}'")
                .WithErrors(downloadResult);
        }

        var cleaner = new VersionedFolderCleaner(_fileSystem, _logger);
        await cleaner.DeleteOtherVersionsAsync(folderPath);

        return Result.Ok();
    }
}
