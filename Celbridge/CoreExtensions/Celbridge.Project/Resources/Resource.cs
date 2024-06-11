using Celbridge.BaseLibrary.Resource;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.Resources;

public abstract partial class Resource : ObservableObject, IResource
{
    protected Resource(string name)
    {
        Name = name;
    }

    [ObservableProperty]
    private string _name = string.Empty;
}
