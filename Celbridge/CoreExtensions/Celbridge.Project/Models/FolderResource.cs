using Celbridge.BaseLibrary.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.Models;

public partial class FolderResource : Resource, IFolderResource
{
    public ObservableCollection<IResource> Children { get; set; }

    [ObservableProperty]
    private bool _expanded = false;

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
