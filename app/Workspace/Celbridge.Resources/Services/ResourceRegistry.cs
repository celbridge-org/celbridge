using System.Text;
using Celbridge.Projects;
using Celbridge.UserInterface;

namespace Celbridge.Resources.Services;

public class ResourceRegistry : IResourceRegistry
{
    private readonly IMessengerService _messengerService;
    private readonly IFileIconService _fileIconService;

    public string ProjectFolderPath { get; set; } = string.Empty;

    private FolderResource _rootFolder = new FolderResource(string.Empty, null);

    public IFolderResource RootFolder => _rootFolder;

    public ResourceRegistry(
        IMessengerService messengerService,
        IFileIconService fileIconService)
    {
        _messengerService = messengerService;
        _fileIconService = fileIconService;
    }

    public ResourceKey GetResourceKey(IResource resource)
    {
        try
        {
            var sb = new StringBuilder();
            void AddResourceKeySegment(IResource resource)
            {
                if (resource.ParentFolder is null)
                {
                    return;
                }

                // Build path by recursively visiting each parent folders
                AddResourceKeySegment(resource.ParentFolder);

                // The trick is to append the path segment after we've visited the parent.
                // This ensures the path segments are appended in the right order.
                if (sb.Length > 0)
                {
                    sb.Append("/");
                }
                sb.Append(resource.Name);
            }
            AddResourceKeySegment(resource);

            var resourceKey = new ResourceKey(sb.ToString());

            return resourceKey;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to get resource key for '{resource}'", ex);
        }
    }

    public Result<ResourceKey> GetResourceKey(string resourcePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(resourcePath);
            var normalizedProjectPath = Path.GetFullPath(ProjectFolderPath);

            if (!normalizedPath.StartsWith(normalizedProjectPath))
            {
                return Result<ResourceKey>.Fail($"The path '{resourcePath}' is not in the project folder '{ProjectFolderPath}'.");
            }

            var resourceKey = normalizedPath.Substring(ProjectFolderPath.Length)
                .Replace('\\', '/')
                .Trim('/');

            return Result<ResourceKey>.Ok(resourceKey);
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"An exception occurred when getting the resource key.")
                .WithException(ex);
        }
    }

    public string GetResourcePath(IResource resource)
    {
        try
        {
            var resourceKey = GetResourceKey(resource);
            var path = GetResourcePath(resourceKey);
            return path;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to get path for resource '{resource}'", ex);
        }
    }

    public string GetResourcePath(ResourceKey resource)
    {
        try
        {
            var resourcePath = Path.Combine(ProjectFolderPath, resource);
            var normalized = Path.GetFullPath(resourcePath);

            return normalized;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to get path for resource '{resource}'.", ex);
        }
    }

    public Result<IResource> GetResource(ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            // An empty resource key refers to the root folder
            return Result<IResource>.Ok(_rootFolder);
        }

        var segments = resource.ToString().Split('/');
        var searchFolder = _rootFolder;

        // Attempt to match each segment with the corresponding resource in the tree
        var segmentIndex = 0;
        while (segmentIndex < segments.Length)
        {
            FolderResource? matchingFolder = null;
            string segment = segments[segmentIndex];
            foreach (var childResource in searchFolder.Children)
            {
                if (childResource is FolderResource childFolder &&
                    childFolder.Name == segment)
                {
                    if (segmentIndex == segments.Length - 1)
                    {
                        // The folder name matches the last segment in the key, so this is the
                        // folder resource we're looking for.
                        return Result<IResource>.Ok(childFolder);
                    }

                    // This folder resource matches this segment in the key, so we can move onto
                    // searching for the next segment.
                    matchingFolder = childFolder;
                    break;
                }
                else if (childResource is FileResource childFile &&
                         childFile.Name == segment &&
                         segmentIndex == segments.Length - 1)
                {
                    // The file name matches the last segment in the key, so this is the
                    // file resource we're looking for.
                    return Result<IResource>.Ok(childFile);
                }
            }

            if (matchingFolder is null)
            {
                break;
            }

            searchFolder = matchingFolder;
            segmentIndex++;
        }

        return Result<IResource>.Fail($"Failed to find a resource matching the resource key '{resource}'.");
    }

    public ResourceKey ResolveDestinationResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        string output = destResource;

        var getResult = GetResource(destResource);
        if (getResult.IsSuccess)
        {
            var resource = getResult.Value;
            if (resource is IFolderResource)
            {
                if (destResource.IsEmpty)
                {
                    // Destination is the root folder
                    output = sourceResource.ResourceName;
                }
                else
                {
                    if (sourceResource == destResource)
                    {
                        // Source and destination are the same folder. This case is allowed because
                        // the user may duplicate a folder by copying and pasting it to the same destination.
                        output = destResource;
                    }
                    else
                    {
                        // Destination is a folder, so append the source resource name to this folder.
                        output = destResource.Combine(sourceResource.ResourceName);
                    }
                }
            }
        }

        return output;
    }

    public ResourceKey ResolveSourcePathDestinationResource(string sourcePath, ResourceKey destResource)
    {
        string output = destResource;

        var getResult = GetResource(destResource);
        if (getResult.IsSuccess)
        {
            var resource = getResult.Value;
            if (resource is IFolderResource)
            {
                var filename = Path.GetFileName(sourcePath);
                if (destResource.IsEmpty)
                {
                    // Destination is the root folder
                    output = filename;
                }
                else
                {
                    // Destination is a folder, so append the source filename to this folder.
                    output = destResource.Combine(filename);
                }
            }
        }

        return output;
    }


    public ResourceKey GetContextMenuItemFolder(IResource? resource)
    {
        IFolderResource? destFolder = null;
        switch (resource)
        {
            case IFolderResource folder:
                destFolder = folder;
                break;
            case IFileResource file:
                destFolder = file.ParentFolder;
                break;
        }
        if (destFolder is null)
        {
            destFolder = _rootFolder;
        }

        return GetResourceKey(destFolder);
    }

    public Result UpdateResourceRegistry()
    {
        try
        {
            SynchronizeFolder(_rootFolder, ProjectFolderPath);

            _messengerService.Send(new ResourceRegistryUpdatedMessage());

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when updating the resource registry.")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Recursively synchronizes a folder resource with the file system.
    /// Always creates fresh resource instances to prevent stale TreeViewNode.Content references
    /// during rapid registry updates (e.g., undo/redo operations).
    /// </summary>
    private void SynchronizeFolder(FolderResource folderResource, string folderPath)
    {
        // Get filtered lists of subfolders and files
        bool isRootFolder = folderResource.ParentFolder is null;
        var subFolderPaths = Directory.GetDirectories(folderPath).OrderBy(d => d).ToList();
        RemoveHiddenFolders(subFolderPaths, isRootFolder);

        var filePaths = Directory.GetFiles(folderPath).OrderBy(f => f).ToList();
        RemoveHiddenFiles(filePaths);

        // Clear and rebuild with fresh instances
        folderResource.Children.Clear();

        // Add subfolder resources (recursive)
        foreach (var subFolderPath in subFolderPaths)
        {
            var folderName = Path.GetFileName(subFolderPath);
            var childFolder = new FolderResource(folderName, folderResource);
            SynchronizeFolder(childFolder, subFolderPath);
            folderResource.AddChild(childFolder);
        }

        // Add file resources
        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var fileExtension = Path.GetExtension(filePath).TrimStart('.');
            
            var getIconResult = _fileIconService.GetFileIconForExtension(fileExtension);
            var iconDefinition = getIconResult.IsSuccess 
                ? getIconResult.Value 
                : _fileIconService.DefaultFileIcon;

            folderResource.AddChild(new FileResource(fileName, folderResource, iconDefinition));
        }

        // Sort children: folders first, then files, both alphabetically
        folderResource.Children = folderResource.Children
            .OrderBy(child => child is IFolderResource ? 0 : 1)
            .ThenBy(child => child.Name)
            .ToList();
    }

    public List<(ResourceKey Resource, string Path)> GetAllFileResources()
    {
        var fileResources = new List<(ResourceKey Resource, string Path)>();
        CollectFileResources(_rootFolder, fileResources);

        // Sort by path for stable ordering
        fileResources.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        return fileResources;
    }

    /// <summary>
    /// Recursively collects all file resources from the resource registry.
    /// </summary>
    private void CollectFileResources(
        IFolderResource folder,
        List<(ResourceKey Resource, string Path)> fileResources)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFileResource fileResource)
            {
                var resourceKey = GetResourceKey(fileResource);
                var filePath = GetResourcePath(resourceKey);
                fileResources.Add((resourceKey, filePath));
            }
            else if (child is IFolderResource childFolder)
            {
                CollectFileResources(childFolder, fileResources);
            }
        }
    }

    public Result<ResourceKey> NormalizeResourceKey(ResourceKey resourceKey)
    {
        try
        {
            var resourcePath = GetResourcePath(resourceKey);

            if (!File.Exists(resourcePath) && !Directory.Exists(resourcePath))
            {
                return Result.Fail($"Resource does not exist: '{resourceKey}'");
            }

            var realPathResult = GetRealPath(resourcePath);
            if (realPathResult.IsFailure)
            {
                return Result.Fail($"Failed to get actual path casing for: '{resourcePath}'")
                    .WithErrors(realPathResult);
            }
            var realPath = realPathResult.Value;

            var normalizedResult = GetResourceKey(realPath);
            if (normalizedResult.IsFailure)
            {
                return Result.Fail($"Failed to normalize resource key: '{resourceKey}'")
                    .WithErrors(normalizedResult);
            }
            var normalizedKey = normalizedResult.Value;

            return normalizedKey;
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when normalizing resource key '{resourceKey}'.")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Returns the actual case-sensitive path from the file system.
    /// Fails if the file does not exist.
    /// </summary>
    private static Result<string> GetRealPath(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                return Result<string>.Fail("Path is null or empty");
            }

            // Get the full path first
            var fullPath = Path.GetFullPath(path);

            // Start with the root (e.g., "C:\")
            var root = Path.GetPathRoot(fullPath);
            if (root == null)
            {
                return Result<string>.Fail($"Could not determine path root for: '{fullPath}'");
            }

            // If the path is just the root, return it as-is
            if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return Result<string>.Ok(fullPath);
            }

            // Get the relative path after the root
            var relativePath = fullPath.Substring(root.Length);
            var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                            StringSplitOptions.RemoveEmptyEntries);

            // Build up the corrected path segment by segment
            var currentPath = root;
            foreach (var segment in segments)
            {
                // Try to find the actual entry with correct casing
                var entries = Directory.GetFileSystemEntries(currentPath, segment);

                if (entries.Length == 0)
                {
                    // This shouldn't happen since we verified the path exists, but handle it gracefully
                    return Result<string>.Fail($"Path segment not found: '{segment}' in '{currentPath}'");
                }

                // Use the first match (there should only be one on case-insensitive systems)
                currentPath = entries[0];
            }

            return Result<string>.Ok(currentPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when getting actual path casing")
                .WithException(ex);
        }
    }

    // Remove hidden folders from a list of folder paths
    private static void RemoveHiddenFolders(List<string> folderPaths, bool isRootFolder)
    {
        folderPaths.RemoveAll(path =>
        {
            if (Path.GetFileName(path).StartsWith('.'))
            {
                // Ignore files or folders that start with a dot.
                // This includes the .celbridge folder which is used to store workspace settings.
                return true;
            }

#if WINDOWS
            var attributes = File.GetAttributes(path);
            if ((attributes & System.IO.FileAttributes.Hidden) != 0)
            {
                // Windows only: Ignore folders with the 'hidden' attribute
                return true;
            }
#endif
            var dirInfo = new DirectoryInfo(path);

            // Ignore the CelData folder
            if (isRootFolder && dirInfo.Name == ProjectConstants.MetaDataFolder)
            {
                return true;
            }

            // Ignore python cache folders
            if (dirInfo.Name == "__pycache__")
            {
                return true;
            }

            // Ignore the Python/Lib folder containing pip packages
            if (dirInfo.Name == "Lib" &&
                dirInfo.Parent?.Name == "Python")
            {
                return true;
            }

            return false;
        });
    }

    // Remove hidden files from a list of folder paths
    private static void RemoveHiddenFiles(List<string> filePaths)
    {
        // Ignore hidden files
        filePaths.RemoveAll(path =>
        {
            if (Path.GetFileName(path).StartsWith('.'))
            {
                // Ignore files that start with a dot.
                return true;
            }

#if WINDOWS
            var attributes = File.GetAttributes(path);
            if ((attributes & System.IO.FileAttributes.Hidden) != 0)
            {
                // Windows only: Ignore files with the 'hidden' attribute
                return true;
            }
#endif

            return false;
        });
    }
}
