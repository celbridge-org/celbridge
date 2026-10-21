namespace Celbridge.Resources;

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
    /// Resolves through IRootHandlerRegistry.ResolveResourcePath, which fails a key
    /// that differs from the name stored on disk for an existing item.
    /// validateCase has no effect.
    /// </summary>
    Result<string> ResolveResourcePath(ResourceKey resource, bool validateCase = true);

    /// <summary>
    /// Returns the resource with the specified resource key.
    /// Fails if no resource matching the resource key is found in the project.
    /// </summary>
    Result<IResource> GetResource(ResourceKey resource);

    /// <summary>
    /// Updates the registry to mirror the current state of the files and folders in the project folder.
    /// </summary>
    Task<Result> UpdateResourceRegistryAsync();
}
