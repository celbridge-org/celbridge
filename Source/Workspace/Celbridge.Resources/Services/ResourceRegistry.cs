using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services.Roots;
using Celbridge.UserInterface;

namespace Celbridge.Resources.Services;

public class ResourceRegistry : IResourceRegistry
{
    private readonly ILogger<ResourceRegistry> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IFileIconService _fileIconService;
    private readonly PathValidator _pathValidator = new();
    private readonly Dictionary<string, IResourceRootHandler> _rootHandlers = new(StringComparer.Ordinal);

    // Sidecar tracking state, refreshed on each UpdateResourceRegistry pass.
    // The report is rebuilt atomically per pass so readers always see a coherent
    // snapshot.
    private readonly object _sidecarLock = new();
    private SidecarReport _sidecarReport = new(
        Healthy: Array.Empty<ResourceKey>(),
        Broken: Array.Empty<ResourceKey>(),
        Orphan: Array.Empty<ResourceKey>());
    private readonly Dictionary<ResourceKey, ResourceKey> _sidecarToParent = new();

    private string _projectFolderPath = string.Empty;

    public string ProjectFolderPath
    {
        get => _projectFolderPath;
        set
        {
            _projectFolderPath = value;
            // Construct (or replace) the project root handler whenever the project folder
            // path is set. ResolveResourcePath delegates to this handler for any project-root key.
            if (!string.IsNullOrEmpty(value))
            {
                RegisterRootHandler(new ProjectRootHandler(value, _pathValidator));
            }
        }
    }

    private FolderResource _projectFolder = new FolderResource(string.Empty, null);

    public IFolderResource ProjectFolder => _projectFolder;

    public IReadOnlyDictionary<string, IResourceRootHandler> RootHandlers => _rootHandlers;

    public ResourceRegistry(
        ILogger<ResourceRegistry> logger,
        IMessengerService messengerService,
        IFileIconService fileIconService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _fileIconService = fileIconService;
    }

    public void RegisterRootHandler(IResourceRootHandler handler)
    {
        _rootHandlers[handler.RootName] = handler;
    }

    public bool IsResolvable(ResourceKey key)
    {
        return _rootHandlers.ContainsKey(key.Root);
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

            var resourceKey = ResourceKey.Create(sb.ToString());

            return resourceKey;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to get resource key for '{resource}'", ex);
        }
    }

    public List<ResourceKey> GetResourceKeys(IEnumerable<IResource> resources)
    {
        return resources.Select(GetResourceKey).ToList();
    }

    public Result<ResourceKey> GetResourceKey(string resourcePath)
    {
        try
        {
            // Cross-root dispatch: find the registered handler whose backing location is
            // the longest prefix (left substring) of the absolute path. Longest-prefix-wins
            // so that a path under .celbridge/temp/ matches the temp handler rather
            // than the project handler (which has the shorter <project>/ prefix). e.g.
            // C:\proj\ (project root, length 8)
            // C:\proj\.celbridge\temp\ (temp root, length 23)
            var normalizedPath = Path.GetFullPath(resourcePath);

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            IResourceRootHandler? bestHandler = null;
            int bestPrefixLength = -1;

            foreach (var handler in _rootHandlers.Values)
            {
                var backing = Path.GetFullPath(handler.BackingLocation)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                bool isBackingRoot = normalizedPath.Equals(backing, comparison);
                bool isUnderBacking = normalizedPath.StartsWith(
                    backing + Path.DirectorySeparatorChar, comparison);

                if ((isBackingRoot || isUnderBacking) && backing.Length > bestPrefixLength)
                {
                    bestHandler = handler;
                    bestPrefixLength = backing.Length;
                }
            }

            if (bestHandler is null)
            {
                return Result<ResourceKey>.Fail(
                    $"The path '{resourcePath}' is not under any registered resource root.");
            }

            return bestHandler.GetResourceKey(normalizedPath);
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"An exception occurred when getting the resource key.")
                .WithException(ex);
        }
    }

    public Result<string> ResolveResourcePath(IResource resource)
    {
        var resourceKey = GetResourceKey(resource);
        return ResolveResourcePath(resourceKey);
    }

    public Result<string> ResolveResourcePath(ResourceKey resource)
    {
        if (!_rootHandlers.TryGetValue(resource.Root, out var handler))
        {
            return Result<string>.Fail(
                $"Resource root '{resource.Root}' is not registered.");
        }
        return handler.Resolve(resource);
    }

    public Result<IResource> GetResource(ResourceKey resource)
    {
        // The registry tracks only the project tree; other roots have no IResource nodes.
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result<IResource>.Fail(
                $"GetResource is scoped to the project tree; root '{resource.Root}' has no tracked resources.");
        }

        if (resource.IsEmpty)
        {
            // An empty resource key refers to the project folder
            return Result<IResource>.Ok(_projectFolder);
        }

        var segments = resource.Path.Split('/');
        var searchFolder = _projectFolder;

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
                    // Destination is the project folder
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
                    // Destination is the project folder
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
            destFolder = _projectFolder;
        }

        return GetResourceKey(destFolder);
    }

    public Result UpdateResourceRegistry()
    {
        try
        {
            // Build a fresh tree off to the side, then atomically swap _projectFolder.
            // Readers on other threads see either the old or the new tree, never a torn
            // intermediate state. Once a tree has been observed it is immutable, so
            // iterators on Children remain valid even if a swap happens during a read.
            // Volatile.Write adds a release fence so the tree's construction writes are
            // visible before the new reference (a no-op on x64, required on ARM64).
            var newRoot = new FolderResource(string.Empty, null);
            SynchronizeFolder(newRoot, ProjectFolderPath);
            UpdateSidecarPairings(newRoot);
            Volatile.Write(ref _projectFolder, newRoot);

            _pathValidator.InvalidateCache();

            try
            {
                _messengerService.Send(new ResourceRegistryUpdatedMessage());
            }
            catch (Exception)
            {
                // Message handlers (e.g., UI tree rebuild) may throw transient
                // exceptions such as COMException when the visual tree is in a
                // transitional state. These are safe to ignore because the
                // registry update itself has already completed successfully.
            }

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
        bool isProjectFolder = folderResource.ParentFolder is null;
        var subFolderPaths = Directory.GetDirectories(folderPath).OrderBy(d => d).ToList();
        RemoveHiddenFolders(subFolderPaths, isProjectFolder);

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
        return GetAllFileResources(ResourceKey.DefaultRoot);
    }

    public List<(ResourceKey Resource, string Path)> GetAllFileResources(string root)
    {
        // Only the project root has an indexed tree in the registry.
        // Other roots (e.g. temp:, logs:) are addressable but not enumerated here.
        if (root != ResourceKey.DefaultRoot)
        {
            return new List<(ResourceKey Resource, string Path)>();
        }

        var fileResources = new List<(ResourceKey Resource, string Path)>();
        CollectFileResources(_projectFolder, fileResources);

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
                var resolveResult = ResolveResourcePath(resourceKey);
                if (resolveResult.IsSuccess)
                {
                    fileResources.Add((resourceKey, resolveResult.Value));
                }
            }
            else if (child is IFolderResource childFolder)
            {
                CollectFileResources(childFolder, fileResources);
            }
        }
    }

    public Result<IFileResource> GetSidecarParent(ResourceKey sidecar)
    {
        if (!sidecar.Path.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return Result<IFileResource>.Fail(
                $"Resource key '{sidecar}' is not a sidecar key (does not end in '{SidecarHelper.Extension}').");
        }

        lock (_sidecarLock)
        {
            if (!_sidecarToParent.TryGetValue(sidecar, out var parentKey))
            {
                return Result<IFileResource>.Fail(
                    $"No parent file is paired with sidecar '{sidecar}'.");
            }

            var resourceResult = GetResource(parentKey);
            if (resourceResult.IsFailure)
            {
                return Result<IFileResource>.Fail(
                    $"Failed to resolve parent file '{parentKey}' for sidecar '{sidecar}'.")
                    .WithErrors(resourceResult);
            }

            if (resourceResult.Value is not IFileResource fileResource)
            {
                return Result<IFileResource>.Fail(
                    $"Parent of sidecar '{sidecar}' is not a file resource.");
            }

            return Result<IFileResource>.Ok(fileResource);
        }
    }

    public SidecarReport GetSidecarReport()
    {
        lock (_sidecarLock)
        {
            return _sidecarReport;
        }
    }

    // Walks the newly-built tree pairing parent files with their .cel sidecars.
    // Runs after SynchronizeFolder so the tree shape is final; sets each
    // FileResource.Sidecar in place and rebuilds the report snapshot.
    private void UpdateSidecarPairings(FolderResource projectRoot)
    {
        var healthy = new List<ResourceKey>();
        var broken = new List<ResourceKey>();
        var orphan = new List<ResourceKey>();
        var newSidecarToParent = new Dictionary<ResourceKey, ResourceKey>();

        ProcessFolder(projectRoot);

        var newReport = new SidecarReport(
            Healthy: healthy,
            Broken: broken,
            Orphan: orphan);

        lock (_sidecarLock)
        {
            _sidecarToParent.Clear();
            foreach (var entry in newSidecarToParent)
            {
                _sidecarToParent[entry.Key] = entry.Value;
            }
            _sidecarReport = newReport;
        }

        void ProcessFolder(FolderResource folder)
        {
            // Build a name lookup for siblings so the pairing checks are O(1) per file.
            var siblingByName = new Dictionary<string, IResource>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in folder.Children)
            {
                siblingByName[child.Name] = child;
            }

            foreach (var child in folder.Children)
            {
                if (child is FolderResource subFolder)
                {
                    ProcessFolder(subFolder);
                    continue;
                }

                if (child is not FileResource fileResource)
                {
                    continue;
                }

                ClassifyFile(fileResource, siblingByName);
            }
        }

        void ClassifyFile(
            FileResource fileResource,
            Dictionary<string, IResource> siblingByName)
        {
            var name = fileResource.Name;

            // Files ending in .cel.cel are never paired with anything. They are
            // surfaced as Broken so the user can resolve them.
            if (name.EndsWith(".cel.cel", StringComparison.OrdinalIgnoreCase))
            {
                fileResource.Sidecar = null;
                broken.Add(GetResourceKey(fileResource));
                return;
            }

            if (name.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
            {
                ClassifySidecarFile(fileResource, siblingByName);
                return;
            }

            // Non-sidecar file: pair with the sibling <name>.cel if it exists.
            var sidecarName = name + SidecarHelper.Extension;
            if (siblingByName.TryGetValue(sidecarName, out var sibling)
                && sibling is FileResource siblingFile
                && !siblingFile.Name.EndsWith(".cel.cel", StringComparison.OrdinalIgnoreCase))
            {
                var sidecarKey = GetResourceKey(siblingFile);

                // The sidecar's classification may not have run yet; populate a
                // placeholder Healthy entry now and let ClassifySidecarFile
                // overwrite it with the inspected status when it runs.
                var existingStatus = fileResource.Sidecar?.Status ?? SidecarStatus.Healthy;
                fileResource.Sidecar = new SidecarInfo(sidecarKey, existingStatus);
                return;
            }

            fileResource.Sidecar = null;
        }

        void ClassifySidecarFile(
            FileResource sidecarFile,
            Dictionary<string, IResource> siblingByName)
        {
            var sidecarName = sidecarFile.Name;
            var parentName = sidecarName.Substring(0, sidecarName.Length - SidecarHelper.Extension.Length);

            var sidecarKey = GetResourceKey(sidecarFile);

            // Inspect the .cel file's content to determine its status. Broken
            // bytes are never modified on disk; the user repairs them by hand.
            var resolveResult = ResolveResourcePath(sidecarKey);
            SidecarStatus status;
            if (resolveResult.IsFailure)
            {
                _logger.LogWarning($"sidecar pairing: failed to resolve path for '{sidecarKey}'");
                status = SidecarStatus.Broken;
            }
            else
            {
                status = SidecarHelper.Inspect(resolveResult.Value, _logger);
            }

            // A .cel file has no sidecar of its own (sidecars don't have sidecars).
            sidecarFile.Sidecar = null;

            // Pair with the parent if present.
            if (siblingByName.TryGetValue(parentName, out var parentSibling)
                && parentSibling is FileResource parentFile)
            {
                newSidecarToParent[sidecarKey] = GetResourceKey(parentFile);
                parentFile.Sidecar = new SidecarInfo(sidecarKey, status);
            }
            else
            {
                orphan.Add(sidecarKey);
            }

            if (status == SidecarStatus.Healthy)
            {
                healthy.Add(sidecarKey);
            }
            else
            {
                broken.Add(sidecarKey);
            }
        }
    }

    public Result<ResourceKey> NormalizeResourceKey(ResourceKey resourceKey)
    {
        try
        {
            var resolveResult = ResolveResourcePath(resourceKey);
            if (resolveResult.IsFailure)
            {
                return Result.Fail($"Failed to resolve path for resource key: '{resourceKey}'")
                    .WithErrors(resolveResult);
            }
            var resourcePath = resolveResult.Value;

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
    private static void RemoveHiddenFolders(List<string> folderPaths, bool isProjectFolder)
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
            if (isProjectFolder && dirInfo.Name == ProjectConstants.MetaDataFolder)
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
