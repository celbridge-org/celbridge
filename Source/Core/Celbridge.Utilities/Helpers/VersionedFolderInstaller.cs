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

        await DeleteOtherVersionsAsync(folderPath);

        return Result.Ok();
    }

    // Deletes only the folders named by a version, so another copy of Celbridge keeps the temporary folder of a
    // download in progress. A folder that fails to delete is logged and stays in place.
    private async Task DeleteOtherVersionsAsync(string installedFolderPath)
    {
        var versionsFolderPath = Path.GetDirectoryName(installedFolderPath);
        if (string.IsNullOrEmpty(versionsFolderPath))
        {
            return;
        }
        var installedVersion = Path.GetFileName(installedFolderPath);

        var enumerateResult = await _fileSystem.EnumerateAsync(versionsFolderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            _logger.LogWarning(enumerateResult, $"Failed to list the versions in '{versionsFolderPath}'");
            return;
        }

        foreach (var entry in enumerateResult.Value)
        {
            var folderName = Path.GetFileName(entry.FullPath);
            if (!entry.IsFolder
                || folderName == installedVersion
                || !SemanticVersion.TryParse(folderName, out _))
            {
                continue;
            }

            var deleteResult = await _fileSystem.DeleteFolderAsync(entry.FullPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                _logger.LogWarning(deleteResult, $"Failed to delete the folder '{entry.FullPath}'");
                continue;
            }

            _logger.LogInformation("Deleted the folder '{Folder}'", entry.FullPath);
        }
    }
}
