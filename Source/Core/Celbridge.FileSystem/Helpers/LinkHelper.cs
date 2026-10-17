namespace Celbridge.FileSystem;

/// <summary>
/// Decides whether an item on disk is a link. A link is a symbolic link or a junction, whether or not its
/// target exists. Celbridge never follows one.
/// </summary>
public static class LinkHelper
{
    /// <summary>
    /// Returns true if the item is a link. The item must exist.
    /// </summary>
    public static bool IsLink(FileSystemInfo item)
    {
        // Only a link pays for reading its target. Other reparse points, such as cloud placeholders, have
        // no link target.
        if (!item.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
        {
            return false;
        }

        try
        {
            return item.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat an item with an unreadable target as a link, so Celbridge never follows it.
            return true;
        }
    }

    /// <summary>
    /// Returns true if the item at the path is a link. A missing item is not a link.
    /// </summary>
    public static bool IsLink(string path)
    {
        // A missing item reports every attribute flag, so it is ruled out before the flags are read.
        var item = new FileInfo(path);
        if (!item.Exists
            && !Directory.Exists(path))
        {
            return false;
        }

        return IsLink(item);
    }
}
