namespace Celbridge.Resources;

/// <summary>
/// A data structure representing the resources in the project folder.
/// </summary>
public interface IResourceRegistry
{
    /// <summary>
    /// The path of the project folder.
    /// </summary>
    string ProjectFolderPath { get; set; }

    /// <summary>
    /// The root folder resource that contains all the resources in the project.
    /// </summary>
    IFolderResource RootFolder { get; }

    /// <summary>
    /// Returns the resource key for a resource.
    /// </summary>
    ResourceKey GetResourceKey(IResource resource);

    /// <summary>
    /// Returns the resource key for a resource at the specified path in the project.
    /// The resource key will be generated even if the resource does not exist yet in the project.
    /// Fails if the path is not within the project folder.
    /// </summary>
    Result<ResourceKey> GetResourceKey(string resourcePath);

    /// <summary>
    /// Returns the absolute path for a resource.
    /// </summary>
    string GetResourcePath(IResource resource);

    /// <summary>
    /// Returns the absolute path for a resource key.
    /// The path will be generated even if the resource does not exist yet in the project.
    /// </summary>
    string GetResourcePath(ResourceKey resource);

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
    /// Returns a resolved destination resource key for a resource transfer.
    /// If destResource specifies an existing folder in the project, then the name of the source resource is
    /// appended to the destination folder resource. In all other situations, destResource is returned unchanged.
    /// </summary>
    ResourceKey ResolveDestinationResource(ResourceKey sourceResource, ResourceKey destResource);

    /// <summary>
    /// Returns a resolved destination resource key for a resource transfer from a source path to a destination resource.
    /// If destResource specifies an existing folder in the project, then the name of the source resource is
    /// appended to the destination folder resource. In all other situations, destResource is returned unchanged.
    /// </summary>
    ResourceKey ResolveSourcePathDestinationResource(string sourcePath, ResourceKey destResource);

    /// <summary>
    /// Returns the folder resource associated with the context menu item for a resource.
    /// If the resource is a folder, then the folder is returned.
    /// If the resource is a file, then the file's parent folder is returned.
    /// If the resource is null, then the root folder is returned.
    /// </summary>
    ResourceKey GetContextMenuItemFolder(IResource? resource);

    /// <summary>
    /// Updates the registry to mirror the current state of the files and folders in the project folder.
    /// </summary>
    Result UpdateResourceRegistry();

    /// <summary>
    /// Returns all file resources in the registry with their resource keys and absolute paths.
    /// The results are sorted by path for stable ordering.
    /// </summary>
    List<(ResourceKey Resource, string Path)> GetAllFileResources();
}
