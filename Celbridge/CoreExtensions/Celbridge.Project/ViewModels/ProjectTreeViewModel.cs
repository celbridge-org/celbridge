using Celbridge.Project.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectTreeViewModel : ObservableObject
{
    private ObservableCollection<Resource> _children = new();
    public ObservableCollection<Resource> Children
    {
        get
        {
            return _children;
        }
        set
        {
            SetProperty(ref _children, value);
        }
    }

    public ProjectTreeViewModel()
    {
        Children = new()
        {
            new FolderResource()
            {
                Name = "Flavors",
                Children = new()
                {
                    new FileResource() { Name = "Vanilla" },
                    new FileResource() { Name = "Strawberry" },
                    new FileResource() { Name = "Chocolate" }
                }
            },
            new FolderResource()
            {
                Name = "Toppings",
                Children = new()
                {
                    new FolderResource()
                    {
                        Name = "Candy",
                        Children = new()
                        {
                            new FileResource() { Name = "Chocolate" },
                            new FileResource() { Name = "Mint" },
                            new FileResource() { Name = "Sprinkles" }
                        }
                    },
                }
            }
        };
    }
}
