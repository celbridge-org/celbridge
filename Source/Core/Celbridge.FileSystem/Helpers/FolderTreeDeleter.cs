namespace Celbridge.FileSystem;

/// <summary>
/// Deletes a folder and everything in it without following a link. Each link is removed, and its target is kept.
/// Directory.Delete with recursive set fails on Windows for a folder that contains a junction, after removing the
/// junction.
/// </summary>
internal static class FolderTreeDeleter
{
    /// <summary>
    /// Deletes the folder at the path and everything in it, clearing read-only attributes on the way. A folder
    /// that is itself a link is removed alone. Throws DirectoryNotFoundException for a missing folder.
    /// </summary>
    public static void Delete(string path)
    {
        var folder = new DirectoryInfo(path);
        if (!folder.Exists)
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{path}'.");
        }

        if (LinkHelper.IsLink(folder))
        {
            folder.Delete();
            return;
        }

        DeleteTree(folder);
    }

    /// <summary>
    /// Deletes the folder at the path as Delete does. A missing folder is left as it is.
    /// </summary>
    public static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Delete(path);
        }
    }

    private static void DeleteTree(DirectoryInfo folder)
    {
        // Clear the folder's read-only attribute before deleting its children. On macOS and Linux, deleting an
        // entry needs a writable parent folder.
        ClearReadOnlyAttribute(folder);

        foreach (var entry in folder.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            // Delete a link without following it.
            bool isLink = LinkHelper.IsLink(entry);
            if (entry is DirectoryInfo childFolder
                && !isLink)
            {
                DeleteTree(childFolder);
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
