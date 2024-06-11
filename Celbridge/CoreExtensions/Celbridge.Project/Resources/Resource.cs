using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.Resources;

public abstract partial class Resource : ObservableObject
{
    protected Resource(string name)
    {
        Name = name;
    }

    [ObservableProperty]
    private string _name = string.Empty;
}
