namespace Celbridge.FileSystem;

/// <summary>
/// Moves a folder to another volume by copying the tree and then deleting the source. Refuses a tree that
/// contains a link before copying anything, because the copy would replace the link with its target. A
/// failed move leaves the source intact.
/// </summary>
internal static class CrossVolumeFolderMove
{
    // A rename between volumes fails in one of three ways. On Windows, .NET compares the two path roots
    // before it touches the disk and throws its own generic IOException.
    private const int DotNetIOExceptionHResult = unchecked((int)0x80131620);

    // A junction or mount point that leads to another volume passes that check and fails with
    // ERROR_NOT_SAME_DEVICE.
    private const int NotSameDeviceHResult = unchecked((int)0x80070011);

    // On macOS and Linux the rename fails with EXDEV, and .NET passes the errno through as the HResult.
    private const int CrossDeviceLinkErrno = 18;

    // Returns true if a folder rename failed because the source and destination are on different volumes.
    public static bool IsCrossVolumeFailure(Exception exception, string source, string dest)
    {
        if (exception is not IOException)
        {
            return false;
        }

        if (exception.HResult == NotSameDeviceHResult
            || exception.HResult == CrossDeviceLinkErrno)
        {
            return true;
        }

        // .NET throws the same generic IOException when a folder is moved onto itself, so compare the roots.
        var sourceRoot = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(source));
        var destRoot = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(dest));
        bool rootsDiffer = !string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        return exception.HResult == DotNetIOExceptionHResult
            && rootsDiffer;
    }

    // Blocks while copying. Call it from a worker thread.
    public static Result Move(string source, string dest, ILogger logger)
    {
        logger.LogDebug($"Moving folder to another volume by copying it: '{source}' to '{dest}'");

        var sourceFolder = new DirectoryInfo(source);

        string? linkPath;
        try
        {
            linkPath = FindLink(sourceFolder);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to read folder before moving it to another volume: '{source}'")
                .WithException(ex);
        }

        if (linkPath is not null)
        {
            return Result.Fail($"Failed to move folder to another volume because it contains a link: '{linkPath}'");
        }

        if (Directory.Exists(dest)
            || File.Exists(dest))
        {
            return Result.Fail($"Failed to move folder because the destination already exists: '{dest}'");
        }

        try
        {
            CopyFolderTree(sourceFolder, dest);
        }
        catch (Exception copyException)
        {
            RemoveCopy(dest, logger);

            return Result.Fail($"Failed to copy folder to another volume: '{source}' to '{dest}'")
                .WithException(copyException);
        }

        // Rename the source on its own volume first, then delete it. If another program has a file open, the
        // rename fails and leaves the source intact. A delete could fail partway and leave it half deleted.
        var asidePath = GetAsidePath(source);
        var asideResult = RetryPolicy.Run(logger, "Move", source, () => Directory.Move(source, asidePath));
        if (asideResult.IsFailure)
        {
            RemoveCopy(dest, logger);

            return Result.Fail($"Failed to move folder to another volume because the source could not be moved: '{source}'")
                .WithErrors(asideResult);
        }

        // The destination copy is complete, so a failed delete only leaves the old source behind.
        var deleteResult = RetryPolicy.Run(logger, "Delete", asidePath, () => DeleteFolderTreeIfExists(asidePath));
        if (deleteResult.IsFailure)
        {
            logger.LogWarning(deleteResult, $"Moved folder to another volume but failed to delete the source, which is left at: '{asidePath}'");
        }

        return Result.Ok();
    }

    // Deletes the destination copy after a failed move. Only the source remains.
    private static void RemoveCopy(string dest, ILogger logger)
    {
        var removeResult = RetryPolicy.Run(logger, "Delete", dest, () => DeleteFolderTreeIfExists(dest));
        if (removeResult.IsFailure)
        {
            logger.LogWarning(removeResult, $"Failed to remove the copy of a folder moved to another volume: '{dest}'");
        }
    }

    // Returns a path beside the source, so the rename stays on the source's volume. The name doesn't include
    // the source's name, so a long source name can't push the path past the file system's limit.
    private static string GetAsidePath(string source)
    {
        var fullSourcePath = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(source));
        var parentPath = System.IO.Path.GetDirectoryName(fullSourcePath)!;

        return System.IO.Path.Combine(parentPath, $".moving-{Guid.NewGuid():N}");
    }

    // Returns the path of the first link in the tree, or null if there is none. Doesn't descend into links.
    private static string? FindLink(DirectoryInfo folder)
    {
        if (folder.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
        {
            return folder.FullName;
        }

        foreach (var entry in folder.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            if (entry is DirectoryInfo childFolder)
            {
                var childLinkPath = FindLink(childFolder);
                if (childLinkPath is not null)
                {
                    return childLinkPath;
                }
            }
            else if (entry.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
            {
                return entry.FullName;
            }
        }

        return null;
    }

    // File.Copy keeps a file's attributes. Copy a folder's attributes explicitly, so a hidden folder such as
    // .git stays hidden.
    private static void CopyFolderTree(DirectoryInfo sourceFolder, string destPath)
    {
        Directory.CreateDirectory(destPath);

        foreach (var entry in sourceFolder.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            var entryDestPath = System.IO.Path.Combine(destPath, entry.Name);
            if (entry is DirectoryInfo childFolder)
            {
                CopyFolderTree(childFolder, entryDestPath);
            }
            else
            {
                File.Copy(entry.FullName, entryDestPath);
            }
        }

        var keptAttributes = sourceFolder.Attributes
            & (System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System | System.IO.FileAttributes.ReadOnly);
        if (keptAttributes != 0)
        {
            var destFolder = new DirectoryInfo(destPath);
            destFolder.Attributes |= keptAttributes;
        }
    }

    // A rename moves read-only entries too, so clear the read-only attribute before deleting each entry.
    private static void DeleteFolderTreeIfExists(string path)
    {
        var folder = new DirectoryInfo(path);
        if (folder.Exists)
        {
            DeleteFolderTree(folder);
        }
    }

    private static void DeleteFolderTree(DirectoryInfo folder)
    {
        // Clear the folder's read-only attribute before deleting its children. On macOS and Linux, deleting an
        // entry needs a writable parent folder.
        ClearReadOnlyAttribute(folder);

        foreach (var entry in folder.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            // Delete a link without following it.
            bool isLink = entry.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
            if (entry is DirectoryInfo childFolder
                && !isLink)
            {
                DeleteFolderTree(childFolder);
                continue;
            }

            if (!isLink)
            {
                ClearReadOnlyAttribute(entry);
            }
            entry.Delete();
        }

        folder.Delete();
    }

    private static void ClearReadOnlyAttribute(FileSystemInfo entry)
    {
        if (entry.Attributes.HasFlag(System.IO.FileAttributes.ReadOnly))
        {
            entry.Attributes &= ~System.IO.FileAttributes.ReadOnly;
        }
    }
}
