using Celbridge.FilePicker;
using Celbridge.Projects;
using Celbridge.Settings;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private const int MaxLocationLength = 80;

    private readonly IStringLocalizer _stringLocalizer;
    private readonly IEditorSettings _editorSettings;
    private readonly IProjectService _projectService;
    private readonly IFilePickerService _filePickerService;

    [ObservableProperty]
    private bool _isCreateButtonEnabled;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _destFolderPath = string.Empty;

    [ObservableProperty]
    private bool _createSubfolder = true;

    [ObservableProperty]
    private string _destProjectFilePath = string.Empty;

    [ObservableProperty]
    private string _projectSaveLocation = string.Empty;

    public NewProjectConfig? NewProjectConfig { get; private set; }

    public NewProjectDialogViewModel(
        IStringLocalizer stringLocalizer,
        IEditorSettings editorSettings,
        IProjectService projectService,
        IFilePickerService filePickerService)
    {
        _stringLocalizer = stringLocalizer;
        _editorSettings = editorSettings;
        _projectService = projectService;
        _filePickerService = filePickerService;

        _destFolderPath = _editorSettings.PreviousNewProjectFolderPath;

        PropertyChanged += NewProjectDialogViewModel_PropertyChanged;
    }

    private void NewProjectDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DestFolderPath) && Path.Exists(DestFolderPath))
        {
            // Remember the newly selected destination folder
            var trimmedPath = DestFolderPath.TrimEnd('/').TrimEnd('\\');
           _editorSettings.PreviousNewProjectFolderPath = trimmedPath;
        }

        if (e.PropertyName == nameof(DestFolderPath) ||
            e.PropertyName == nameof(ProjectName) ||
            e.PropertyName == nameof(CreateSubfolder))
        {

            if (!ResourceKey.IsValidSegment(ProjectName))
            {
                // Project name is not a valid filename
                IsCreateButtonEnabled = false;
                DestProjectFilePath = string.Empty;
                return;
            }

            string destProjectFilePath;

            if (CreateSubfolder)
            {
                var subfolderPath = Path.Combine(DestFolderPath, ProjectName);
                if (Directory.Exists(subfolderPath))
                {
                    // A subfolder with this name already exists
                    IsCreateButtonEnabled = false;
                    DestProjectFilePath = string.Empty;
                    return;
                }

                destProjectFilePath = Path.Combine(subfolderPath, $"{ProjectName}{FileNames.ProjectFileExtension}");
            }
            else
            {
                destProjectFilePath = Path.Combine(DestFolderPath, $"{ProjectName}{FileNames.ProjectFileExtension}");
            }

            if (File.Exists(destProjectFilePath)) 
            { 
                // A project file with the same name already exists
                IsCreateButtonEnabled = false;
                DestProjectFilePath = string.Empty;
                return;
            }

            IsCreateButtonEnabled = true;
            DestProjectFilePath = destProjectFilePath;
        }

        if (e.PropertyName == nameof(DestProjectFilePath))
        {
            ProjectSaveLocation = DestProjectFilePath;

            if (DestProjectFilePath.Length <= MaxLocationLength)
            {
                ProjectSaveLocation = DestProjectFilePath;
            }
            else
            {
                int clippedLength = DestProjectFilePath.Length - MaxLocationLength + 3; // 3 for ellipses
                ProjectSaveLocation = "..." + DestProjectFilePath.Substring(clippedLength);
            }
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
                DestFolderPath = pickResult.Value;
            }
        }
    }

    public ICommand CreateProjectCommand => new RelayCommand(CreateCommand_Execute);
    private void CreateCommand_Execute()
    {
        var config = new NewProjectConfig(DestProjectFilePath);
        if (_projectService.ValidateNewProjectConfig(config).IsSuccess)
        {
            // If the config is not valid then NewProjectConfig will remain null
            NewProjectConfig = config;
        }
    }
}
