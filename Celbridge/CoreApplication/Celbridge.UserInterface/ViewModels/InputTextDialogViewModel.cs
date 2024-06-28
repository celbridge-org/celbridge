namespace Celbridge.UserInterface.ViewModels;

public partial class InputTextDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;
}
