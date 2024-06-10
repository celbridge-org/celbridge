using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.ViewModels.Dialogs;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private readonly IEditorSettings _editorSettings;
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
        IEditorSettings editorSettings,
        IProjectDataService projectDataService,
        IUserInterfaceService userInterfaceService)
    {
        _editorSettings = editorSettings;
        _projectDataService = projectDataService;
        _userInterfaceService = userInterfaceService;

        _projectFolder = _editorSettings.PreviousNewProjectFolder;

        PropertyChanged += NewProjectDialogViewModel_PropertyChanged;
    }

    private void NewProjectDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var parentFolderExists = Directory.Exists(ProjectFolder);
        var projectFolderExists = Directory.Exists(Path.Combine(ProjectFolder, ProjectName));

        // Todo: Validate that the project name is valid on all platforms
        var projectNameIsValid = !projectFolderExists && !string.IsNullOrWhiteSpace(ProjectName);

        // Todo: Show a message explaining why the create button is disabled
        IsCreateButtonEnabled = parentFolderExists && projectNameIsValid;

        if (e.PropertyName == nameof(ProjectFolder) && parentFolderExists)
        {
            // Remember the newly selected folder
            _editorSettings.PreviousNewProjectFolder = ProjectFolder;
        }
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
        var createResult = await _projectDataService.CreateProjectDataAsync(ProjectFolder, ProjectName);
        if (createResult.IsSuccess)
        {
            // Populate the property if the project created successfully
            ProjectDataPath = createResult.Value;
        }
    }
}
