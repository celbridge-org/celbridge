using Celbridge.FilePicker;
using Celbridge.Projects;
using Celbridge.Settings;
using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class NewProjectDialogViewModel : ObservableObject
{
    private const int MaxLocationLength = 80;
    private const int ValidationDebounceMilliseconds = 200;

    private readonly ISettingsService _settingsService;
    private readonly IProjectService _projectService;
    private readonly IFilePickerService _filePickerService;
    private readonly IProjectTemplateService _templateService;
    private readonly ILocalFileSystem _fileSystem;

    private CancellationTokenSource? _validationCts;

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
        ISettingsService settingsService,
        IProjectService projectService,
        IFilePickerService filePickerService,
        IProjectTemplateService templateService,
        ILocalFileSystem fileSystem)
    {
        _settingsService = settingsService;
        _projectService = projectService;
        _filePickerService = filePickerService;
        _templateService = templateService;
        _fileSystem = fileSystem;

        // Initialize templates
        _templates = _templateService.GetTemplates();

        // Try to restore the previously selected template
        var previousTemplateName = _settingsService.Get(SettingCatalog.Project.PreviousNewProjectTemplateName);
        if (!string.IsNullOrEmpty(previousTemplateName))
        {
            _selectedTemplate = _templates.FirstOrDefault(t => t.Name == previousTemplateName);
        }

        // Fall back to default template if persisted template doesn't exist
        _selectedTemplate ??= _templateService.GetDefaultTemplate();

        PropertyChanged += NewProjectDialogViewModel_PropertyChanged;
    }

    // Resolves the default destination folder and runs the first validation pass.
    // The dialog calls this before it is shown so the folder probes go through the
    // async gateway instead of blocking the constructor on a stat.
    public async Task InitializeAsync()
    {
        // Default folder fallback chain:
        // 1. Previous project folder, if it exists.
        // 2. The user's Documents folder, if it exists.
        // 3. The previous path as-is, which may be invalid (validation disables Create).
        var previousFolder = _settingsService.Get(SettingCatalog.Project.PreviousNewProjectFolderPath);
        if (!string.IsNullOrEmpty(previousFolder)
            && await FolderExistsAsync(previousFolder))
        {
            DestFolderPath = previousFolder;
            return;
        }

        var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (await FolderExistsAsync(documentsFolder))
        {
            DestFolderPath = documentsFolder;
            return;
        }

        DestFolderPath = previousFolder ?? string.Empty;
    }

    private void NewProjectDialogViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DestFolderPath) && Path.Exists(DestFolderPath))
        {
            // Remember the newly selected destination folder
            var trimmedPath = DestFolderPath.TrimEnd('/').TrimEnd('\\');
            _settingsService.Set(SettingCatalog.Project.PreviousNewProjectFolderPath, trimmedPath);
        }

        if (e.PropertyName == nameof(DestFolderPath) ||
            e.PropertyName == nameof(ProjectName) ||
            e.PropertyName == nameof(CreateSubfolder))
        {
            ScheduleValidation();
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

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        var pickResult = await _filePickerService.PickSingleFolderAsync();
        if (pickResult.IsSuccess)
        {
            var folder = pickResult.Value;
            var infoResult = await _fileSystem.GetInfoAsync(folder);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.Folder)
            {
                DestFolderPath = pickResult.Value;
            }
        }
    }

    private void ScheduleValidation()
    {
        // Cancel the previous pass and open a fresh debounce window. The old token
        // source is left for the garbage collector rather than disposed, because
        // its in-flight ValidateAsync may still hold the token.
        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        _ = ValidateAsync(_validationCts.Token);
    }

    // Debounced, latest-wins validation. A newer change cancels the prior token,
    // so a superseded pass bails at the delay or before applying its result.
    // Continuations resume on the UI thread, so compute-then-apply is effectively
    // atomic and the newest pass wins.
    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ValidationDebounceMilliseconds, cancellationToken);

            var outcome = await ComputeValidationAsync();
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ApplyValidationOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change; discard this pass.
        }
    }

    private async Task<ValidationOutcome> ComputeValidationAsync()
    {
        if (!ResourceKey.IsValidSegment(ProjectName))
        {
            return ValidationOutcome.Invalid("NewProjectDialog_InvalidProjectName");
        }

        if (!await FolderExistsAsync(DestFolderPath))
        {
            return ValidationOutcome.Invalid("NewProjectDialog_InvalidFolderPath");
        }

        string destProjectFilePath;
        if (CreateSubfolder)
        {
            var subfolderPath = Path.Combine(DestFolderPath, ProjectName);
            if (await FolderExistsAsync(subfolderPath))
            {
                return ValidationOutcome.Invalid("NewProjectDialog_SubfolderAlreadyExists");
            }
            destProjectFilePath = Path.Combine(subfolderPath, $"{ProjectName}{ProjectConstants.ProjectFileExtension}");
        }
        else
        {
            destProjectFilePath = Path.Combine(DestFolderPath, $"{ProjectName}{ProjectConstants.ProjectFileExtension}");
        }

        if (await FileExistsAsync(destProjectFilePath))
        {
            return ValidationOutcome.Invalid("NewProjectDialog_ProjectFileAlreadyExists");
        }

        return ValidationOutcome.Valid(destProjectFilePath);
    }

    private void ApplyValidationOutcome(ValidationOutcome outcome)
    {
        IsCreateButtonEnabled = outcome.CanCreate;
        DestProjectFilePath = outcome.DestProjectFilePath;
        ValidationErrorMessage = outcome.ErrorMessageKey;

        // The error stays hidden until the user has typed a name, so an empty
        // dialog does not open showing a validation error.
        IsValidationErrorVisible = !outcome.CanCreate
            && !string.IsNullOrEmpty(ProjectName);
    }

    private async Task<bool> FolderExistsAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var infoResult = await _fileSystem.GetInfoAsync(path);
        return infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder;
    }

    private async Task<bool> FileExistsAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var infoResult = await _fileSystem.GetInfoAsync(path);
        return infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
    }

    // Outcome of one validation pass: whether Create is allowed, the resolved
    // project file path (empty when invalid), and the localization key for the
    // error message (empty when valid).
    private sealed record ValidationOutcome(
        bool CanCreate,
        string DestProjectFilePath,
        string ErrorMessageKey)
    {
        public static ValidationOutcome Valid(string destProjectFilePath) =>
            new(true, destProjectFilePath, string.Empty);

        public static ValidationOutcome Invalid(string errorMessageKey) =>
            new(false, string.Empty, errorMessageKey);
    }

    [RelayCommand]
    private void CreateProject()
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
            _settingsService.Set(SettingCatalog.Project.PreviousNewProjectTemplateName, SelectedTemplate.Name);
        }

        // The dialog closes automatically after the Create button is clicked.
    }
}

