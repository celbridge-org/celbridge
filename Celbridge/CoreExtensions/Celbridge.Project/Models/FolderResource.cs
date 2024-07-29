using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Projects.Models;

public partial class FolderResource : Resource, IFolderResource
{
    public IList<IResource> Children { get; set; }

    [ObservableProperty]
    private bool _isExpanded = false;

    public FolderResource(string name, FolderResource? parentFolder) 
        : base(name, parentFolder)
    {
        Children = new List<IResource>();
    }

    public void AddChild(IResource resource)
    {
        Children.Add(resource);
    }
}
