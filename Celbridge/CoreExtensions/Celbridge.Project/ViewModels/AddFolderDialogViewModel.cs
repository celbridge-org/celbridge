using Celbridge.BaseLibrary.FilePicker;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class AddFolderDialogViewModel : ObservableObject
{
    private readonly IEditorSettings _editorSettings;
    private readonly IProjectDataService _projectDataService;
    private readonly IFilePickerService _filePickerService;

    [ObservableProperty]
    private bool _isCreateButtonEnabled;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _projectFolder = string.Empty;

    public NewProjectConfig? NewProjectConfig { get; private set; }

    public AddFolderDialogViewModel(
        IEditorSettings editorSettings,
        IProjectDataService projectDataService,
        IFilePickerService filePickerService)
    {
        _editorSettings = editorSettings;
        _projectDataService = projectDataService;
        _filePickerService = filePickerService;

        _projectFolder = _editorSettings.PreviousNewProjectFolder;

        PropertyChanged += NewProjectDialogViewModel_PropertyChanged;
    }

    private void NewProjectDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var parentFolderExists = Directory.Exists(ProjectFolder);
        var projectFolderExists = Directory.Exists(Path.Combine(ProjectFolder, ProjectName));

        var validateResult = _projectDataService.ValidateProjectName(ProjectName);

        // Todo: Show a message explaining why the create button is disabled
        IsCreateButtonEnabled = validateResult.IsSuccess && parentFolderExists && !projectFolderExists;

        if (e.PropertyName == nameof(ProjectFolder) && parentFolderExists)
        {
            // Remember the newly selected folder
            _editorSettings.PreviousNewProjectFolder = ProjectFolder;
        }
    }

    public ICommand SelectFolderCommand => new AsyncRelayCommand(SelectFolderCommand_ExecuteAsync);
    private async Task SelectFolderCommand_ExecuteAsync()
    {
        var pickResult = await _filePickerService.PickSingleFolderAsync();
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
        var config = new NewProjectConfig(ProjectName, ProjectFolder);
        if (_projectDataService.ValidateNewProjectConfig(config).IsSuccess)
        {
            // If the config is not valid then NewProjectConfig will remain null
            NewProjectConfig = config;
        }
    }
}
