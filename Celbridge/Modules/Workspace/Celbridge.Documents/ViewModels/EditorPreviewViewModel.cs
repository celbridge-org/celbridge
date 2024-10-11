using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class EditorPreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private string _previewHTML = string.Empty;

    public EditorPreviewViewModel()
    {}
}
