namespace Celbridge.Resources.Models;

public class FileResource : Resource, IFileResource
{
    public string IconGlyph { get; } = "u";

    public string IconColor { get; } = "LightBlue";

    public FileResource(string name, IFolderResource parentFolder) 
        : base(name, parentFolder)
    {}
}
