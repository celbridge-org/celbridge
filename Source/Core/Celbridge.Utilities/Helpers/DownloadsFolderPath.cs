using Celbridge.FileSystem;
using Celbridge.Projects;
using Celbridge.Resources;

namespace Celbridge.Utilities;

/// <summary>
/// Reads the folder a project saves its downloads to, which its project file names as a path from the
/// project root. A project that names none uses the default folder.
/// </summary>
public static class DownloadsFolderPath
{
    /// <summary>
    /// The folder downloads are saved to when the project names no other.
    /// </summary>
    public static ResourceKey DefaultFolder { get; } = new ResourceKey(ProjectConstants.DownloadsFolder);

    /// <summary>
    /// Parses a folder path, ignoring whitespace and slashes at either end. Fails for the project root, a path
    /// that is not a valid resource path, one under a root other than the project's, and one inside a folder
    /// Celbridge reserves.
    /// </summary>
    public static bool TryParse(string path, out ResourceKey folder)
    {
        if (!SettingFolderPath.TryParse(path, out folder)
            || SettingFolderPath.IsReserved(folder))
        {
            folder = ResourceKey.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if the path names a .git folder at any depth, or a folder inside one. Celbridge
    /// reserves these folders, so nothing can be saved there.
    /// </summary>
    public static bool IsReserved(string path)
    {
        return SettingFolderPath.TryParse(path, out var folder)
            && SettingFolderPath.IsReserved(folder);
    }

    /// <summary>
    /// The folder downloads are saved to in the project. A folder that exists is returned as the project
    /// spells it, which can differ in case; a path with nothing at it is returned unchanged, for the first
    /// download to create. The default folder is used when the path is empty, invalid, reserved, or held
    /// by a file.
    /// </summary>
    public static ResourceKey Resolve(IResourceRegistry registry, ILocalFileSystem fileSystem, string path)
    {
        if (TryParse(path, out var folder))
        {
            var namedFolder = LocateFolder(registry, fileSystem, folder);
            if (namedFolder is not null)
            {
                return namedFolder.Value;
            }
        }

        return LocateFolder(registry, fileSystem, DefaultFolder) ?? DefaultFolder;
    }

    // Where the folder is: as the project spells it when it exists, the path as given when nothing is
    // there, and null when a file holds the path, because no folder can be made there.
    private static ResourceKey? LocateFolder(IResourceRegistry registry, ILocalFileSystem fileSystem, ResourceKey folder)
    {
        var normalizeResult = registry.NormalizeResourceKey(folder);
        if (normalizeResult.IsFailure)
        {
            return folder;
        }
        var resourceOnDisk = normalizeResult.Value;

        var getResourceResult = registry.GetResource(resourceOnDisk);
        if (getResourceResult.IsSuccess)
        {
            if (getResourceResult.Value is IFolderResource)
            {
                return resourceOnDisk;
            }

            return null;
        }

        // On disk but not yet in the registry, such as the folder WebView2 creates for the download that is
        // asking where to go. The disk says whether it is a folder. The key already has the disk's case, so the
        // path needs no second case check.
        var resolveResult = registry.ResolveResourcePath(resourceOnDisk, validateCase: false);
        if (resolveResult.IsFailure)
        {
            return null;
        }

        var infoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(resolveResult.Value));
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder)
        {
            return resourceOnDisk;
        }

        return null;
    }
}
