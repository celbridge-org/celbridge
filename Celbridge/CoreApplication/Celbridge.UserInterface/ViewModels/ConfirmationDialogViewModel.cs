namespace Celbridge.UserInterface.ViewModels;

public partial class ConfirmationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _messageText = string.Empty;
}
