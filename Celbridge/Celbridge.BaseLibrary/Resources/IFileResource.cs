using Celbridge.UserInterface;

namespace Celbridge.Resources;

/// <summary>
/// A file resource in the project folder.
/// </summary>
public interface IFileResource : IResource
{
    public IconDefinition Icon { get; }
}
