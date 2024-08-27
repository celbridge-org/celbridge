using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentTabViewModel : ObservableObject
{
    [ObservableProperty]
    public string _name = "Default";

    internal ResourceKey ResourceKey { get; set; }
    internal string FilePath { get; set; } = string.Empty;
}
