namespace Celbridge.Resources.Models;

public class FileResource : Resource, IFileResource
{
    public FileResource(string name, IFolderResource parentFolder) 
        : base(name, parentFolder)
    { }
}
