namespace Celbridge.FileSystem;

/// <summary>
/// Moves a folder to another volume, where a rename cannot reach, by copying the tree and then deleting
/// the source. A tree that holds a link is refused before anything is copied, since a copy would replace
/// the link with what it points at. A move that fails leaves the source whole.
/// </summary>
internal static class CrossVolumeFolderMove
{
    // A rename between volumes fails in one of three ways. On Windows, .NET compares the two path roots
    // before it touches the disk and throws a plain IOException of its own.
    private const int DotNetIOExceptionHResult = unchecked((int)0x80131620);

    // A junction or mount point that leads to another volume passes that check and fails with
    // ERROR_NOT_SAME_DEVICE.
    private const int NotSameDeviceHResult = unchecked((int)0x80070011);

    // On macOS and Linux the rename fails with EXDEV, and .NET passes the errno through as the HResult.
    private const int CrossDeviceLinkErrno = 18;

    // Whether a folder rename failed because the source and destination are on different volumes.
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

        // .NET throws the same plain IOException for a move onto the source itself, so the roots decide.
        var sourceRoot = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(source));
        var destRoot = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(dest));
        bool rootsDiffer = !string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        return exception.HResult == DotNetIOExceptionHResult
            && rootsDiffer;
    }

    // Blocks while it copies, so the caller runs it on a worker thread.
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
            return Result.Fail($"Failed to move folder to another volume because it holds a link: '{linkPath}'");
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

        // The source is renamed aside on its own volume, then deleted. A delete could fail partway and leave
        // the source half deleted. The rename fails whole instead, as when another program has a file open.
        var asidePath = GetAsidePath(source);
        var asideResult = RetryPolicy.Run(logger, "Move", source, () => Directory.Move(source, asidePath));
        if (asideResult.IsFailure)
        {
            RemoveCopy(dest, logger);

            return Result.Fail($"Failed to move folder to another volume because the source could not be moved: '{source}'")
                .WithErrors(asideResult);
        }

        // The folder is whole at its destination by now, so a delete that fails only leaves the old copy behind.
        var deleteResult = RetryPolicy.Run(logger, "Delete", asidePath, () => DeleteFolderTreeIfExists(asidePath));
        if (deleteResult.IsFailure)
        {
            logger.LogWarning(deleteResult, $"Moved folder to another volume but failed to delete the source, which is left at: '{asidePath}'");
        }

        return Result.Ok();
    }

    // Removing the copy leaves the source as the only copy, as it was before the move.
    private static void RemoveCopy(string dest, ILogger logger)
    {
        var removeResult = RetryPolicy.Run(logger, "Delete", dest, () => DeleteFolderTreeIfExists(dest));
        if (removeResult.IsFailure)
        {
            logger.LogWarning(removeResult, $"Failed to remove the copy of a folder moved to another volume: '{dest}'");
        }
    }

    // A sibling of the source keeps the rename on the source's volume. The name does not depend on the
    // source's, so a long name cannot push it past the file system's limit.
    private static string GetAsidePath(string source)
    {
        var fullSourcePath = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(source));
        var parentPath = System.IO.Path.GetDirectoryName(fullSourcePath)!;

        return System.IO.Path.Combine(parentPath, $".moving-{Guid.NewGuid():N}");
    }

    // The path of the first link in the tree, or null when it holds none. The walk never enters a link.
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

    // File.Copy keeps a file's attributes. A folder keeps those that change how it shows or what it allows,
    // so a hidden folder such as .git stays hidden.
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

    // A rename ignores read-only entries, so the delete that stands in for one clears the attribute first.
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
        // Cleared before the children, because on macOS and Linux deleting an entry needs a writable folder.
        ClearReadOnlyAttribute(folder);

        foreach (var entry in folder.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            // A link is deleted as itself, never followed.
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
