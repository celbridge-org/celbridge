namespace Celbridge.ViewModels.Dialogs;

public partial class NewProjectDialogViewModel : ObservableObject
{
    public ICommand CreateProjectCommand => new RelayCommand(CreateCommand_Execute);
    private void CreateCommand_Execute()
    {
    }
}
