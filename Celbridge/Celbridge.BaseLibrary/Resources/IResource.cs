namespace Celbridge.BaseLibrary.Resource;

/// <summary>
/// A file or folder resource in the project folder.
/// </summary>
public interface IResource
{
    /// <summary>
    /// The name of the file or folder resource.
    /// </summary>
    string Name { get; }
}
