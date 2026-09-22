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

    // The folders Celbridge reserves, matched as the resource policy matches them: by name, at any depth.
    private static readonly ResourcePathMatcher[] ReservedFolderMatchers =
    {
        ResourcePathMatcher.Compile(ProjectConstants.CelbridgeFolder),
        ResourcePathMatcher.Compile(ProjectConstants.GitFolder),
    };

    /// <summary>
    /// Parses a folder path, ignoring whitespace and slashes at either end. Fails for an empty path, one
    /// that is not a valid resource path, one under a root other than the project's, and one inside a folder
    /// Celbridge reserves.
    /// </summary>
    public static bool TryParse(string path, out ResourceKey folder)
    {
        if (!TryParseProjectPath(path, out folder)
            || IsReservedFolder(folder))
        {
            folder = ResourceKey.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the path names a folder inside one Celbridge reserves, .celbridge or .git at any depth. Nothing
    /// can be saved there, so the path is not a downloads folder.
    /// </summary>
    public static bool IsReserved(string path)
    {
        return TryParseProjectPath(path, out var folder)
            && IsReservedFolder(folder);
    }

    /// <summary>
    /// The folder downloads are saved to in the project. A folder the project has is taken as the project
    /// spells it, which can differ from the path in case, and a path with nothing at it yet is taken as it
    /// is, for the first download to create. The default folder serves when the path is empty, not valid or
    /// reserved, or when a file holds it.
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

    private static bool TryParseProjectPath(string path, out ResourceKey folder)
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

    private static bool IsReservedFolder(ResourceKey folder)
    {
        return ReservedFolderMatchers.Any(matcher => matcher.IsMatch(folder.Path, isFolder: true));
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
