namespace Celbridge.Project.Models;

public class FileResource : Resource
{
    public FileResource(string name, FolderResource parentFolder) 
        : base(name, parentFolder)
    { }
}
