namespace Celbridge.Resources;

/// <summary>
/// A folder resource in the project folder.
/// </summary>
public interface IFolderResource : IResource
{
    /// <summary>
    /// The child resources of the folder.
    /// </summary>
    IList<IResource> Children { get; set; }
}
