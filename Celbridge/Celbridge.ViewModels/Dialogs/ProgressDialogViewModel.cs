namespace Celbridge.ViewModels.Dialogs;

public partial class ProgressDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _cancelText = string.Empty;

    public ICommand CancelCommand => new RelayCommand(CancelCommand_Executed);
    private void CancelCommand_Executed()
    {
        // Todo: Implement cancel button
        // Todo: Add a general system for managing progress dialogs, progress tokens in a list
    }
}
