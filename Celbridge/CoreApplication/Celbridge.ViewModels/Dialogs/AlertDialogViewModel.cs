namespace Celbridge.ViewModels.Dialogs;

public partial class AlertDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private string _closeText = string.Empty;
}
