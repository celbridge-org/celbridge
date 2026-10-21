namespace Celbridge.Resources;

/// <summary>
/// A data structure representing the resources in the project folder.
/// </summary>
public interface IResourceRegistry
{
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
    /// Returns the resource with the specified resource key.
    /// Fails if no resource matching the resource key is found in the project.
    /// </summary>
    Result<IResource> GetResource(ResourceKey resource);

    /// <summary>
    /// Updates the registry to mirror the current state of the files and folders in the project folder.
    /// </summary>
    Task<Result> UpdateResourceRegistryAsync();
}
