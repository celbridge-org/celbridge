namespace Celbridge.Resources;

/// <summary>
/// Snapshot of the .cel files in the project tree, partitioned by parse state
/// and orphan-ness. Produced by the classifier on every UpdateResourceRegistry
/// pass; consumed by project-load diagnostics and the data_inspect tool.
/// Parse state (Healthy / Broken) and orphan-ness are orthogonal: an orphan
/// .cel file with malformed content appears in both Broken and Orphan. Files
/// ending in .cel.cel are surfaced as Broken and are never treated as sidecars.
/// </summary>
public record SidecarReport(
    IReadOnlyList<ResourceKey> Healthy,
    IReadOnlyList<ResourceKey> Broken,
    IReadOnlyList<ResourceKey> Orphan);

/// <summary>
/// A file resource paired with its absolute filesystem path, as returned by
/// IResourceRegistry.GetAllFileResources.
/// </summary>
public record FileResourceEntry(ResourceKey Resource, string Path);

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
    /// Returns the resource key for an absolute filesystem path under any registered root.
    /// The resource key is generated even if no resource exists at that path yet.
    /// Fails when the path is not under any registered root.
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
    /// When validateCase is true (the default) a project-root key whose on-disk
    /// case differs from the supplied case is rejected; pass false on the listing
    /// path, where child keys are re-derived from disk-canonical names anyway.
    /// </summary>
    Result<string> ResolveResourcePath(ResourceKey resource, bool validateCase = true);

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
    Task<Result> UpdateResourceRegistryAsync();

    /// <summary>
    /// Returns all file resources for the project root with their resource keys and absolute paths.
    /// The results are sorted by path for stable ordering.
    /// </summary>
    IReadOnlyList<FileResourceEntry> GetAllFileResources();

    /// <summary>
    /// Returns all file resources for the specified root with their resource keys and absolute paths.
    /// Returns an empty list for roots without indexed tree state.
    /// </summary>
    IReadOnlyList<FileResourceEntry> GetAllFileResources(string root);

    /// <summary>
    /// Returns the SidecarReport from the last completed UpdateResourceRegistry
    /// pass. Project-load diagnostics and the data_inspect tool consume this to
    /// surface broken and orphan .cel files.
    /// </summary>
    SidecarReport GetSidecarReport();
}
