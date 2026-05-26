namespace Celbridge.Resources;

/// <summary>
/// Snapshot of every .cel-shaped file the registry knows about, partitioned by
/// parse state and orphan-ness. Used for project-load diagnostics and by
/// data_check_project to surface attention states.
///
/// Parse state (Healthy / Broken) and orphan-ness are orthogonal dimensions:
/// an orphan .cel file with malformed content appears in both Broken and Orphan.
/// Files whose names end in .cel.cel are classified as Broken and never as a
/// regular sidecar.
/// </summary>
public record SidecarReport(
    IReadOnlyList<ResourceKey> Healthy,
    IReadOnlyList<ResourceKey> Broken,
    IReadOnlyList<ResourceKey> Orphan);

/// <summary>
/// A data structure representing the resources in the project folder.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>
    /// The path of the project folder. Empty until InitializeProjectRoot is called.
    /// </summary>
    string ProjectFolderPath { get; }

    /// <summary>
    /// Sets the project folder path and registers the project root handler.
    /// </summary>
    void InitializeProjectRoot(string projectFolderPath);

    /// <summary>
    /// The project folder resource that contains all the resources in the project.
    /// </summary>
    IFolderResource ProjectFolder { get; }

    /// <summary>
    /// Returns the resource key for a resource.
    /// </summary>
    ResourceKey GetResourceKey(IResource resource);

    /// <summary>
    /// Returns resource keys for multiple resources.
    /// </summary>
    List<ResourceKey> GetResourceKeys(IEnumerable<IResource> resources);

    /// <summary>
    /// Returns the resource key for a resource at the specified path in the project.
    /// The resource key will be generated even if the resource does not exist yet in the project.
    /// Fails if the path is not within the project folder.
    /// </summary>
    Result<ResourceKey> GetResourceKey(string resourcePath);

    /// <summary>
    /// Resolves a resource to its absolute filesystem path.
    /// Validates path containment and checks for symlinks/junctions.
    /// Fails if the path escapes the project folder or traverses a reparse point.
    /// </summary>
    Result<string> ResolveResourcePath(IResource resource);

    /// <summary>
    /// Resolves a resource key to its absolute filesystem path.
    /// Validates path containment and checks for symlinks/junctions.
    /// The path will be generated even if the resource does not exist yet in the project.
    /// Fails if the path escapes the project folder or traverses a reparse point.
    /// </summary>
    Result<string> ResolveResourcePath(ResourceKey resource);

    /// <summary>
    /// Normalizes the resource key so that it matches the exact casing as it exists on disk.
    /// Fails if no resource matching the resource key is found in the project (case-insensitive comparison).
    /// </summary>
    Result<ResourceKey> NormalizeResourceKey(ResourceKey resourceKey);

    /// <summary>
    /// Returns the resource with the specified resource key.
    /// Fails if no resource matching the resource key is found in the project.
    /// </summary>
    Result<IResource> GetResource(ResourceKey resource);

    /// <summary>
    /// Updates the registry to mirror the current state of the files and folders in the project folder.
    /// </summary>
    Result UpdateResourceRegistry();

    /// <summary>
    /// Registers a handler for the specified root name. The handler takes effect
    /// immediately; subsequent resolution calls for that root delegate to it.
    /// Replaces any handler previously registered for the same root name.
    /// </summary>
    void RegisterRootHandler(IResourceRootHandler handler);

    /// <summary>
    /// The currently registered root handlers, keyed by root name.
    /// </summary>
    IReadOnlyDictionary<string, IResourceRootHandler> RootHandlers { get; }

    /// <summary>
    /// Returns true if the resource key's root is registered with this registry.
    /// Use this for early validation at trust boundaries without performing a full resolve.
    /// </summary>
    bool IsResolvable(ResourceKey key);

    /// <summary>
    /// Returns all file resources for the project root with their resource keys and absolute paths.
    /// The results are sorted by path for stable ordering.
    /// </summary>
    List<(ResourceKey Resource, string Path)> GetAllFileResources();

    /// <summary>
    /// Returns all file resources for the specified root with their resource keys and absolute paths.
    /// Returns an empty list for roots without indexed tree state.
    /// </summary>
    List<(ResourceKey Resource, string Path)> GetAllFileResources(string root);

    /// <summary>
    /// Returns the parent file resource of a sidecar key, or a failure result
    /// if the sidecar has no corresponding parent. Sidecars at "foo.png.cel"
    /// resolve to "foo.png"; sidecars whose name ends in ".cel.cel" are invalid
    /// and never have a parent.
    /// </summary>
    Result<IFileResource> GetSidecarParent(ResourceKey sidecar);

    /// <summary>
    /// Returns a snapshot of every sidecar the registry knows about, partitioned
    /// by parse state, orphan-ness, and the .cel.cel invalid category. Used for
    /// project-load diagnostics and by data_check_project.
    /// </summary>
    SidecarReport GetSidecarReport();
}
