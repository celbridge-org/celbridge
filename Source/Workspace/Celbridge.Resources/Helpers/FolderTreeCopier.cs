namespace Celbridge.Resources.Helpers;

/// <summary>
/// Copies a folder and everything in it through the local file system. Links are left out, since a copy would
/// replace a link with what the link points at.
/// </summary>
internal static class FolderTreeCopier
{
    /// <summary>
    /// Copies the folder at the source path to the destination path, creating the destination. The files in a
    /// folder are copied before the folders in it. A failure stops the copy and leaves what was copied in place.
    /// </summary>
    public static async Task<Result> CopyAsync(ILocalFileSystem fileSystem, string sourceFolder, string destFolder)
    {
        var createResult = await fileSystem.CreateFolderAsync(destFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var enumerateResult = await fileSystem.EnumerateAsync(sourceFolder, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail(enumerateResult);
        }

        var entries = enumerateResult.Value
            .Where(entry => !entry.IsLink)
            .ToList();

        foreach (var entry in entries)
        {
            if (entry.IsFolder)
            {
                continue;
            }
            var fileName = Path.GetFileName(entry.FullPath);
            var destFile = Path.Combine(destFolder, fileName);
            var copyResult = await fileSystem.CopyFileAsync(entry.FullPath, destFile);
            if (copyResult.IsFailure)
            {
                return copyResult;
            }
        }

        foreach (var entry in entries)
        {
            if (!entry.IsFolder)
            {
                continue;
            }
            var folderName = Path.GetFileName(entry.FullPath);
            var destSubFolder = Path.Combine(destFolder, folderName);
            var recursiveResult = await CopyAsync(fileSystem, entry.FullPath, destSubFolder);
            if (recursiveResult.IsFailure)
            {
                return recursiveResult;
            }
        }

        return Result.Ok();
    }
}
