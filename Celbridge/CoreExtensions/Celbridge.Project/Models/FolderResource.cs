using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Projects.Models;

public partial class FolderResource : Resource, IFolderResource
{
    public ObservableCollection<IResource> Children { get; set; }

    [ObservableProperty]
    private bool _isExpanded = false;

    public FolderResource(string name, FolderResource? parentFolder) 
        : base(name, parentFolder)
    {
        Children = new();
    }

    public void AddChild(IResource resource)
    {
        Children.Add(resource);
    }
}
