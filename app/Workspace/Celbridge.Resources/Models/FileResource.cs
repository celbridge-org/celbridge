using Celbridge.UserInterface;

namespace Celbridge.Resources.Models;

public class FileResource : Resource, IFileResource
{
    public FileIconDefinition Icon { get; }

    public FileResource(string name, IFolderResource parentFolder, FileIconDefinition icon) 
        : base(name, parentFolder)
    {
        Icon = icon;
    }
}
