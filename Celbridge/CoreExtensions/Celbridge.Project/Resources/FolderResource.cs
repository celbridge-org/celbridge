using System.Collections.ObjectModel;

namespace Celbridge.Project.Resources;

public class FolderResource : Resource
{
    public ObservableCollection<Resource> Children { get; set; }

    public FolderResource(string name) : base(name)
    {
        Children = new ObservableCollection<Resource>();
    }

    public void AddChild(Resource resource)
    {
        Children.Add(resource);
    }
}
