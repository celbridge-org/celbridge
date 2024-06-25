using Celbridge.BaseLibrary.Resources;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.Models;

public abstract partial class Resource : ObservableObject, IResource
{
    protected Resource(string name)
    {
        Guard.IsNotNullOrWhiteSpace(name);
        Name = name;
    }

    [ObservableProperty]
    private string _name = string.Empty;
}
