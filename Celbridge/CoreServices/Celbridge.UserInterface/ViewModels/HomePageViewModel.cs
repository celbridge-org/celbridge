using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.ViewModels;

public record RecentProject(string ProjectFolderPath, string ProjectName);

public partial class HomePageViewModel : ObservableObject
{
    private readonly Logging.ILogger<HomePageViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IEditorSettings _editorSettings;
    private readonly MainMenuUtils _mainMenuUtils;

    public HomePageViewModel(
        INavigationService navigationService,
        Logging.ILogger<HomePageViewModel> logger,
        ICommandService commandService,
        IEditorSettings editorSettings,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        MainMenuUtils mainMenuUtils)
    {
        _logger = logger;
        _commandService = commandService;
        _editorSettings = editorSettings;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _mainMenuUtils = mainMenuUtils;

        PopulateRecentProjects();
    }

    private void PopulateRecentProjects()
    {
        var recentProjects = _editorSettings.RecentProjects;
        foreach (var recentProject in recentProjects)
        {
            if (!File.Exists(recentProject))
            {
                continue;
            }

            try
            {
                var projectFolderPath = Path.GetDirectoryName(recentProject);
                var projectName = Path.GetFileName(recentProject);
                projectName = Path.GetFileNameWithoutExtension(projectName);

                if (!string.IsNullOrEmpty(projectFolderPath))
                {
                    RecentProjects.Add(new RecentProject(projectFolderPath, projectName));
                }
            }
            catch (Exception)
            {
                continue;
            }
        }
    }

    public List<RecentProject> RecentProjects = new();

    public IAsyncRelayCommand NewProjectCommand => new AsyncRelayCommand(NewProjectCommand_Executed);
    private async Task NewProjectCommand_Executed()
    {
        await _mainMenuUtils.ShowNewProjectDialogAsync();
    }

    public IAsyncRelayCommand OpenProjectCommand => new AsyncRelayCommand(OpenProjectCommand_Executed);
    private async Task OpenProjectCommand_Executed()
    {
        await _mainMenuUtils.ShowOpenProjectDialogAsync();
    }

    public void OpenProject(string projectFilePath)
    {
        if (!File.Exists(projectFilePath)) 
        {
            _logger.LogError($"Project file does not exist: {projectFilePath}");
            return;
        }

        _commandService.Execute<ILoadProjectCommand>((command) =>
        {
            command.ProjectFilePath = projectFilePath;
        });
    }
}

