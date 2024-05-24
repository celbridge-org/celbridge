using Celbridge.BaseLibrary.Project;

namespace Celbridge.ViewModels.Dialogs;

public partial class NewProjectDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCreateButtonEnabled;

    [ObservableProperty]
    private string _projectName = string.Empty;

    private readonly IProjectManagerService _projectManagerService;

    public NewProjectDialogViewModel(IProjectManagerService projectManagerService)
    {
        _projectManagerService = projectManagerService;

        PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(ProjectName))
            {
                // Todo: Check if project name is valid
                IsCreateButtonEnabled = !string.IsNullOrWhiteSpace(ProjectName);
            }
        };
    }

    public ICommand CreateProjectCommand => new RelayCommand(CreateCommand_Execute);
    private void CreateCommand_Execute()
    {
        _projectManagerService.CreateProject(ProjectName);
    }
}
