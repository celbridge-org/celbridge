using Celbridge.BaseLibrary.Resources;

namespace Celbridge.Project.Models;

public class FileResource : Resource, IFileResource
{
    public FileResource(string name, IFolderResource parentFolder) 
        : base(name, parentFolder)
    { }
}
