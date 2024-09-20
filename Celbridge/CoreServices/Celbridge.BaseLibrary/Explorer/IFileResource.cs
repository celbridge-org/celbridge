using Celbridge.UserInterface;

namespace Celbridge.Explorer;

/// <summary>
/// A file resource in the project folder.
/// </summary>
public interface IFileResource : IResource
{
    /// <summary>
    /// The icon to display for the file resource.
    /// </summary>
    public IconDefinition Icon { get; }
}
