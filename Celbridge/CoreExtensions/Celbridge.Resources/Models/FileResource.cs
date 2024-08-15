using Celbridge.UserInterface;

namespace Celbridge.Resources.Models;

public class FileResource : Resource, IFileResource
{
    public IconDefinition Icon { get; }

    public FileResource(string name, IFolderResource parentFolder, IconDefinition icon) 
        : base(name, parentFolder)
    {
        Icon = icon;
    }
}
