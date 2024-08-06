using Celbridge.FilePicker;
using Celbridge.Resources;
using Celbridge.Settings;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private readonly IEditorSettings _editorSettings;
    private readonly IProjectDataService _projectDataService;
    private readonly IFilePickerService _filePickerService;

    [ObservableProperty]
    private bool _isCreateButtonEnabled;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _projectFolderPath = string.Empty;

    public NewProjectConfig? NewProjectConfig { get; private set; }

    public NewProjectDialogViewModel(
        IEditorSettings editorSettings,
        IProjectDataService projectDataService,
        IFilePickerService filePickerService)
    {
        _editorSettings = editorSettings;
        _projectDataService = projectDataService;
        _filePickerService = filePickerService;

        _projectFolderPath = _editorSettings.PreviousNewProjectFolderPath;

        PropertyChanged += NewProjectDialogViewModel_PropertyChanged;
    }

    private void NewProjectDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var parentFolderExists = Directory.Exists(ProjectFolderPath);
        var projectFolderExists = Directory.Exists(Path.Combine(ProjectFolderPath, ProjectName));

        if (e.PropertyName == nameof(ProjectFolderPath) ||
            e.PropertyName == nameof(ProjectName))
        {
            var isValid = ResourceKey.IsValidSegment(ProjectName);

            // Todo: Show a message explaining why the create button is disabled
            IsCreateButtonEnabled = isValid && parentFolderExists && !projectFolderExists;
        }

        if (e.PropertyName == nameof(ProjectFolderPath) && parentFolderExists)
        {
            // Remember the newly selected folder
            _editorSettings.PreviousNewProjectFolderPath = ProjectFolderPath;
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
                ProjectFolderPath = pickResult.Value;
            }
        }
    }

    public ICommand CreateProjectCommand => new RelayCommand(CreateCommand_Execute);
    private void CreateCommand_Execute()
    {
        var config = new NewProjectConfig(ProjectName, ProjectFolderPath);
        if (_projectDataService.ValidateNewProjectConfig(config).IsSuccess)
        {
            // If the config is not valid then NewProjectConfig will remain null
            NewProjectConfig = config;
        }
    }
}
