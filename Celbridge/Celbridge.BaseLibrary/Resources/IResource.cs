namespace Celbridge.BaseLibrary.Resources;

/// <summary>
/// A file or folder resource in the project folder.
/// </summary>
public interface IResource
{
    /// <summary>
    /// The name of the resource.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The folder resource that contains this resource.
    /// </summary>
    public IFolderResource? ParentFolder { get; }
}
