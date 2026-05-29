using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services.Roots;

namespace Celbridge.Resources.Services;

public class ResourceRegistry : IResourceRegistry
{
    private readonly ILogger<ResourceRegistry> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectTreeBuilder _projectTreeBuilder;
    private readonly IResourceClassifier _resourceClassifier;
    private readonly RootHandlerRegistry _rootHandlerRegistry;

    // Sidecar tracking state, refreshed on each UpdateResourceRegistry pass.
    // The report is rebuilt atomically per pass so readers always see a coherent
    // snapshot.
    private readonly object _sidecarLock = new();
    private SidecarReport _sidecarReport = new(
        Healthy: Array.Empty<ResourceKey>(),
        Broken: Array.Empty<ResourceKey>(),
        Orphan: Array.Empty<ResourceKey>());

    private string _projectFolderPath = string.Empty;

    public string ProjectFolderPath => _projectFolderPath;

    public void InitializeProjectRoot(string projectFolderPath)
    {
        Guard.IsNotNullOrEmpty(projectFolderPath);

        _projectFolderPath = projectFolderPath;
        _rootHandlerRegistry.RegisterRootHandler(
            new ProjectRootHandler(projectFolderPath));
    }

    private FolderResource _projectFolder = new FolderResource(string.Empty, null);

    public IFolderResource ProjectFolder => _projectFolder;

    public ResourceRegistry(
        ILogger<ResourceRegistry> logger,
        IMessengerService messengerService,
        IProjectTreeBuilder projectTreeBuilder,
        IResourceClassifier resourceClassifier,
        RootHandlerRegistry rootHandlerRegistry)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectTreeBuilder = projectTreeBuilder;
        _resourceClassifier = resourceClassifier;
        _rootHandlerRegistry = rootHandlerRegistry;
    }

    public ResourceKey GetResourceKey(IResource resource)
    {
        return ResourceTreeNavigator.BuildKey(resource);
    }

    public List<ResourceKey> GetResourceKeys(IEnumerable<IResource> resources)
    {
        return resources.Select(GetResourceKey).ToList();
    }

    public Result<ResourceKey> GetResourceKey(string resourcePath)
    {
        return _rootHandlerRegistry.GetResourceKey(resourcePath);
    }

    public Result<string> ResolveResourcePath(IResource resource)
    {
        var resourceKey = GetResourceKey(resource);
        return ResolveResourcePath(resourceKey);
    }

    public Result<string> ResolveResourcePath(ResourceKey resource)
    {
        var resolveResult = _rootHandlerRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return resolveResult;
        }
        var absolutePath = resolveResult.Value;

        // Strict case enforcement for the project root: if the resolved path
        // exists on disk, the supplied key must match disk-canonical case.
        // Without this guard a Windows user can resolve a wrong-case key
        // (Windows IO is case-insensitive) but the in-memory tree and cascade
        // scanner (both Ordinal-case-sensitive) would treat it as a separate
        // resource, leaving the project in an inconsistent state. The case
        // check requires the project tree, so it stays on the registry rather
        // than moving down into the root handler registry.
        if (resource.Root == ResourceKey.DefaultRoot)
        {
            var caseCheck = EnsureProjectKeyCaseMatchesDisk(resource, absolutePath);
            if (caseCheck.IsFailure)
            {
                return Result<string>.Fail(caseCheck.FirstErrorMessage);
            }
        }

        return absolutePath;
    }

    // Cheap path: if the registry tree already has a node for this exact key,
    // the case is canonical by construction (the tree was built from disk-
    // preserved names). Only when the tree lookup misses do we go to disk to
    // disambiguate "case is wrong" from "resource is new and being created".
    private Result EnsureProjectKeyCaseMatchesDisk(ResourceKey resource, string absolutePath)
    {
        if (resource.IsEmpty)
        {
            return Result.Ok();
        }

        var treeLookup = GetResource(resource);
        if (treeLookup.IsSuccess)
        {
            return Result.Ok();
        }

        // Tree miss. Either the resource is new (not on disk yet — pass
        // through for create flows) or the case is wrong. Check disk to find
        // out which.
        if (!File.Exists(absolutePath)
            && !Directory.Exists(absolutePath))
        {
            return Result.Ok();
        }

        var realPathResult = GetRealPath(absolutePath);
        if (realPathResult.IsFailure)
        {
            // Couldn't determine disk-canonical case for some reason; don't
            // block the operation on the diagnostic.
            return Result.Ok();
        }

        if (string.Equals(realPathResult.Value, absolutePath, StringComparison.Ordinal))
        {
            // Disk case already matches the supplied case — the tree miss is
            // a registry-rebuild lag, not a case mismatch.
            return Result.Ok();
        }

        var canonicalKeyResult = GetResourceKey(realPathResult.Value);
        if (canonicalKeyResult.IsFailure)
        {
            return Result.Fail(
                $"Resource key '{resource}' does not match the on-disk case.");
        }

        return Result.Fail(
            $"Resource key '{resource}' does not match the on-disk case. Canonical form is '{canonicalKeyResult.Value}'.");
    }

    public Result<IResource> GetResource(ResourceKey resource)
    {
        // The registry tracks only the project tree; other roots have no IResource nodes.
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result<IResource>.Fail(
                $"GetResource is scoped to the project tree; root '{resource.Root}' has no tracked resources.");
        }

        return ResourceTreeNavigator.FindResource(_projectFolder, resource);
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
            var newRoot = (FolderResource)_projectTreeBuilder.BuildTree(ProjectFolderPath);

            // Sidecar pairing runs on the new tree before publication. The
            // classifier sets each parent FileResource.Sidecar in place and
            // returns the report, which is swapped under the lock alongside
            // the tree reference. The root handler registry is handed in so
            // per-sidecar path resolution goes through the same reparse-point
            // chokepoint as every other resource operation.
            var sidecarReport = _resourceClassifier.ClassifyResources(newRoot, _rootHandlerRegistry);

            Volatile.Write(ref _projectFolder, newRoot);

            lock (_sidecarLock)
            {
                _sidecarReport = sidecarReport;
            }

            _rootHandlerRegistry.InvalidatePathCache();

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

    public IReadOnlyList<FileResourceEntry> GetAllFileResources()
    {
        return GetAllFileResources(ResourceKey.DefaultRoot);
    }

    public IReadOnlyList<FileResourceEntry> GetAllFileResources(string root)
    {
        // Only the project root has an indexed tree in the registry.
        // Other roots (e.g. temp:, logs:) are addressable but not enumerated here.
        if (root != ResourceKey.DefaultRoot)
        {
            return Array.Empty<FileResourceEntry>();
        }

        var fileResources = new List<FileResourceEntry>();
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
        List<FileResourceEntry> fileResources)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFileResource fileResource)
            {
                var resourceKey = GetResourceKey(fileResource);
                var resolveResult = ResolveResourcePath(resourceKey);
                if (resolveResult.IsSuccess)
                {
                    fileResources.Add(new FileResourceEntry(resourceKey, resolveResult.Value));
                }
            }
            else if (child is IFolderResource childFolder)
            {
                CollectFileResources(childFolder, fileResources);
            }
        }
    }

    public SidecarReport GetSidecarReport()
    {
        lock (_sidecarLock)
        {
            return _sidecarReport;
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
                return fullPath;
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

            return currentPath;
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when getting actual path casing")
                .WithException(ex);
        }
    }

}
