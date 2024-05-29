using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.ViewModels.Dialogs;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private readonly IProjectDataService _projectDataService;
    private readonly IUserInterfaceService _userInterfaceService;

    [ObservableProperty]
    private bool _isCreateButtonEnabled;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _projectFolder = string.Empty;

    public string ProjectDataPath { get; private set; } = string.Empty;

    public NewProjectDialogViewModel(
        IProjectDataService projectDataService,
        IUserInterfaceService userInterfaceService)
    {
        _projectDataService = projectDataService;
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

    public ICommand CreateProjectCommand => new AsyncRelayCommand(CreateCommand_ExecuteAsync);
    private async Task CreateCommand_ExecuteAsync()
    {
        var createResult = await _projectDataService.CreateProjectDataAsync(ProjectFolder, ProjectName, 1);
        if (createResult.IsSuccess)
        {
            // Populate the property if the project created successfully
            ProjectDataPath = createResult.Value;
        }
    }
}
