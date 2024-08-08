namespace Celbridge.Resources.Models;

public class FileResource : Resource, IFileResource
{
    public string IconGlyph { get; set; } = "u";

    public string IconColor { get; set; } = "LightBlue";

    public FileResource(string name, IFolderResource parentFolder) 
        : base(name, parentFolder)
    { }
}
