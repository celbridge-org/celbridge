using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentTabViewModel : ObservableObject
{
    [ObservableProperty]
    public string _name = "Default";
}
