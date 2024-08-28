using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class TextDocumentViewModel : ObservableObject
{
    public string Name => "Text Document";

    [ObservableProperty]
    private string _text = string.Empty;
}
