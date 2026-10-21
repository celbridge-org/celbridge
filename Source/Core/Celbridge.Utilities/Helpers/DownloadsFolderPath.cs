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
    /// Returns the project's downloads folder. A configured folder that does not exist yet is returned unchanged.
    /// The first download creates the folder. The default folder is used when the path is empty, invalid or
    /// reserved, or when a file is at the path. Fails when the path does not match an existing folder's stored name, with a
    /// StoredNameMismatchError that gives the stored name.
    /// </summary>
    public static Result<ResourceKey> Resolve(IRootHandlerRegistry rootHandlerRegistry, ILocalFileSystem fileSystem, string path)
    {
        if (TryParse(path, out var folder))
        {
            var locateResult = LocateFolder(rootHandlerRegistry, fileSystem, folder);
            if (locateResult.IsFailure)
            {
                return Result<ResourceKey>.Fail().WithErrors(locateResult);
            }

            if (locateResult.Value)
            {
                return folder;
            }
        }

        var locateDefaultResult = LocateFolder(rootHandlerRegistry, fileSystem, DefaultFolder);
        if (locateDefaultResult.IsFailure)
        {
            return Result<ResourceKey>.Fail().WithErrors(locateDefaultResult);
        }

        return DefaultFolder;
    }

    // Returns true when a folder or nothing is at the path. Returns false when a file is at the path. No folder can
    // be made there.
    private static Result<bool> LocateFolder(IRootHandlerRegistry rootHandlerRegistry, ILocalFileSystem fileSystem, ResourceKey folder)
    {
        var resolveResult = rootHandlerRegistry.ResolveResourcePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to resolve the downloads folder '{folder}'")
                .WithErrors(resolveResult);
        }

        var infoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(resolveResult.Value));
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File)
        {
            return false;
        }

        return true;
    }
}
