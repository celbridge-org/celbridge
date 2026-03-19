using Celbridge.UserInterface;

namespace Celbridge.Resources;

/// <summary>
/// A file resource in the project folder.
/// </summary>
public interface IFileResource : IResource
{
    /// <summary>
    /// The icon to display for the file resource.
    /// </summary>
    public FileIconDefinition Icon { get; }
}
