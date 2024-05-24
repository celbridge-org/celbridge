using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.ViewModels.Dialogs;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private readonly IProjectManagerService _projectManagerService;
    private readonly IUserInterfaceService _userInterfaceService;

    [ObservableProperty]
    private bool _isCreateButtonEnabled;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _projectFolder = string.Empty;


    public NewProjectDialogViewModel(
        IProjectManagerService projectManagerService,
        IUserInterfaceService userInterfaceService)
    {
        _projectManagerService = projectManagerService;
        _userInterfaceService = userInterfaceService;   

        PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(ProjectName))
            {
                // Todo: Check if project name is valid
                IsCreateButtonEnabled = !string.IsNullOrWhiteSpace(ProjectName);
            }
        };
    }

    public ICommand SelectFolderCommand => new AsyncRelayCommand(SelectFolderCommand_ExecuteAsync);
    private async Task SelectFolderCommand_ExecuteAsync()
    {
        var pickResult = await _userInterfaceService.FilePickerService.PickSingleFolderAsync();
        if (pickResult.IsSuccess)
        {
            var folder = pickResult.Value;
            if (Directory.Exists(folder))
            {
                ProjectFolder = pickResult.Value;
            }
        }
    }

    public ICommand CreateProjectCommand => new RelayCommand(CreateCommand_Execute);
    private void CreateCommand_Execute()
    {
        var path = Path.Combine(ProjectFolder, ProjectName) + ".celbridge";
        _projectManagerService.CreateProject(ProjectFolder, ProjectName);
    }
}
