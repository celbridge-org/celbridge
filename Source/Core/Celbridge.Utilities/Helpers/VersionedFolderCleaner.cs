using Celbridge.FileSystem;
using Celbridge.Logging;
using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Deletes the old versions of a dependency that keeps each version in a folder named by the version.
/// </summary>
public sealed class VersionedFolderCleaner
{
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger _logger;

    public VersionedFolderCleaner(ILocalFileSystem fileSystem, ILogger logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the folders beside the installed version's folder that are named by another version. A folder with
    /// any other name stays, so another copy of Celbridge keeps the temporary folder of a download in progress. A
    /// folder that fails to delete is logged and stays in place.
    /// </summary>
    public async Task DeleteOtherVersionsAsync(string installedFolderPath)
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
