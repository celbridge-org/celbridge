namespace Celbridge.Resources;

/// <summary>
/// A file resource in the project folder.
/// </summary>
public interface IFileResource : IResource
{
    string IconGlyph { get; }

    string IconColor { get; }
}
