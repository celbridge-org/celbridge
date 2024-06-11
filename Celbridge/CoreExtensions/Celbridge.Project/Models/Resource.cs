using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.Models;

public abstract partial class Resource : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;
}
