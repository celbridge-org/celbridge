namespace Celbridge.Legacy.Models;

public class ResourceRegistry
{
    public FolderResource Root { get; private set; }

    public ResourceRegistry()
    {
        Root = new FolderResource();
        Root.Name = "Root";
    }

    public ResourceRegistry(FolderResource root)
    {
        Root = root;
        Root.Name = "Root";
    }
}
