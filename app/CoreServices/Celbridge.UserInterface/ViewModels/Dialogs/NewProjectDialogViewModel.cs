using Celbridge.FilePicker;
using Celbridge.Projects;
using Celbridge.Settings;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private const int MaxLocationLength = 80;

    private readonly IEditorSettings _editorSettings;
    private readonly IProjectService _projectService;
    private readonly IFilePickerService _filePickerService;
    private readonly IProjectTemplateService _templateService;

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

    [ObservableProperty]
    private string _validationErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isValidationErrorVisible = false;

    [ObservableProperty]
    private IReadOnlyList<ProjectTemplate> _templates = [];

    [ObservableProperty]
    private ProjectTemplate? _selectedTemplate;

    public NewProjectConfig? NewProjectConfig { get; private set; }

    public NewProjectDialogViewModel(
        IEditorSettings editorSettings,
        IProjectService projectService,
        IFilePickerService filePickerService,
        IProjectTemplateService templateService)
    {
        _editorSettings = editorSettings;
        _projectService = projectService;
        _filePickerService = filePickerService;
        _templateService = templateService;

        // Initialize templates
        _templates = _templateService.GetTemplates();

        // Try to restore the previously selected template
        if (!string.IsNullOrEmpty(_editorSettings.PreviousNewProjectTemplateName))
        {
            _selectedTemplate = _templates.FirstOrDefault(t => t.Name == _editorSettings.PreviousNewProjectTemplateName);
        }

        // Fall back to default template if persisted template doesn't exist
        _selectedTemplate ??= _templateService.GetDefaultTemplate();

        // Set default path for projects with fallback chain:
        // 1. Previous project folder (if valid)
        // 2. User's Documents folder (if valid)
        // 3. Previous path as-is (may be invalid, but UI will disable Create button)
        if (!string.IsNullOrEmpty(_editorSettings.PreviousNewProjectFolderPath) && 
            Directory.Exists(_editorSettings.PreviousNewProjectFolderPath))
        {
            _destFolderPath = _editorSettings.PreviousNewProjectFolderPath;
        }
        else if (Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)))
        {
            _destFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        else
        {
            _destFolderPath = _editorSettings.PreviousNewProjectFolderPath ?? string.Empty;
        }

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
                ValidationErrorMessage = "NewProjectDialog_InvalidProjectName";
                IsValidationErrorVisible = !string.IsNullOrEmpty(ProjectName);
                return;
            }

            if (!Directory.Exists(DestFolderPath))
            {
                // Project base folder is not valid.
                IsCreateButtonEnabled = false;
                DestProjectFilePath = string.Empty;
                ValidationErrorMessage = "NewProjectDialog_InvalidFolderPath";
                IsValidationErrorVisible = !string.IsNullOrEmpty(ProjectName);
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
                    ValidationErrorMessage = "NewProjectDialog_SubfolderAlreadyExists";
                    IsValidationErrorVisible = !string.IsNullOrEmpty(ProjectName);
                    return;
                }

                destProjectFilePath = Path.Combine(subfolderPath, $"{ProjectName}{ProjectConstants.ProjectFileExtension}");
            }
            else
            {
                destProjectFilePath = Path.Combine(DestFolderPath, $"{ProjectName}{ProjectConstants.ProjectFileExtension}");
            }

            if (File.Exists(destProjectFilePath)) 
            { 
                // A project file with the same name already exists
                IsCreateButtonEnabled = false;
                DestProjectFilePath = string.Empty;
                ValidationErrorMessage = "NewProjectDialog_ProjectFileAlreadyExists";
                IsValidationErrorVisible = !string.IsNullOrEmpty(ProjectName);
                return;
            }

            IsCreateButtonEnabled = true;
            DestProjectFilePath = destProjectFilePath;
            ValidationErrorMessage = string.Empty;
            IsValidationErrorVisible = false;
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

    public IAsyncRelayCommand SelectFolderCommand => new AsyncRelayCommand(SelectFolderCommand_ExecuteAsync);
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

    public ICommand CreateProjectCommand => new RelayCommand(CreateProjectCommand_Execute);
    private void CreateProjectCommand_Execute()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        var config = new NewProjectConfig(DestProjectFilePath, SelectedTemplate);
        if (_projectService.ValidateNewProjectConfig(config).IsSuccess)
        {
            // If the config is not valid then NewProjectConfig will remain null
            NewProjectConfig = config;

            // Persist the template selection only when project is successfully created
            _editorSettings.PreviousNewProjectTemplateName = SelectedTemplate.Name;
        }

        // The dialog closes automatically after the Create button is clicked.
    }
}

