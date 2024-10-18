using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class InspectorViewModel : ObservableObject
{
    [ObservableProperty]
    private ResourceKey _resource;
}
