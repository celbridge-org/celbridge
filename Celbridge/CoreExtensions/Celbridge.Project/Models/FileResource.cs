using Celbridge.Resources;

namespace Celbridge.Projects.Models;

public class FileResource : Resource, IFileResource
{
    public FileResource(string name, IFolderResource parentFolder) 
        : base(name, parentFolder)
    { }
}
