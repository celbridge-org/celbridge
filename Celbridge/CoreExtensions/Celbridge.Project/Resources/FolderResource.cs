using Celbridge.BaseLibrary.Resources;
using System.Collections.ObjectModel;

namespace Celbridge.Project.Resources;

public class FolderResource : Resource
{
    public ObservableCollection<IResource> Children { get; set; }

    public FolderResource(string name) : base(name)
    {
        Children = new();
    }

    public void AddChild(IResource resource)
    {
        Children.Add(resource);
    }
}
