using Celbridge.Projects;
using Celbridge.Resources;

namespace Celbridge.Utilities;

/// <summary>
/// Reads the folder a project saves its downloads to, which its project file names by a path from the
/// project root. A project that names none uses the default folder.
/// </summary>
public static class DownloadsFolderPath
{
    /// <summary>
    /// The folder downloads are saved to when the project names no other.
    /// </summary>
    public static ResourceKey DefaultFolder { get; } = new ResourceKey(ProjectConstants.DownloadsFolder);

    /// <summary>
    /// Parses a folder path, ignoring whitespace and slashes at either end. Fails for an empty path, one
    /// that is not a valid resource path, and one under a root other than the project's.
    /// </summary>
    public static bool TryParse(string path, out ResourceKey folder)
    {
        folder = ResourceKey.Empty;

        var trimmedPath = path.Trim().Trim('/');
        if (!ResourceKey.TryCreate(trimmedPath, out var resource)
            || resource.IsEmpty
            || resource.Root != ResourceKey.DefaultRoot)
        {
            return false;
        }

        folder = resource;
        return true;
    }

    /// <summary>
    /// The folder downloads are saved to in the project. A folder the project has is taken as the project
    /// spells it, which can differ from the path in case, and a path with nothing at it yet is taken as it
    /// is, for the first download to create. The default folder serves when the path is empty or not valid,
    /// or when a file holds it.
    /// </summary>
    public static ResourceKey Resolve(IResourceRegistry registry, string path)
    {
        if (TryParse(path, out var folder))
        {
            var namedFolder = LocateFolder(registry, folder);
            if (namedFolder is not null)
            {
                return namedFolder.Value;
            }
        }

        return LocateFolder(registry, DefaultFolder) ?? DefaultFolder;
    }

    // Where the folder is: as the project spells it when it exists, the path as given when nothing is there
    // yet, and null when a file holds the path, since no folder can be made there.
    private static ResourceKey? LocateFolder(IResourceRegistry registry, ResourceKey folder)
    {
        var normalizeResult = registry.NormalizeResourceKey(folder);
        if (normalizeResult.IsFailure)
        {
            return folder;
        }
        var resourceOnDisk = normalizeResult.Value;

        var getResourceResult = registry.GetResource(resourceOnDisk);
        if (getResourceResult.IsFailure
            || getResourceResult.Value is not IFolderResource)
        {
            return null;
        }

        return resourceOnDisk;
    }
}
