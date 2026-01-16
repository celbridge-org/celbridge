using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class HomePageViewModel : ObservableObject
{
    private readonly Logging.ILogger<HomePageViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IProjectService _projectService;
    private readonly MainMenuUtils _mainMenuUtils;

    public HomePageViewModel(
        INavigationService navigationService,
        Logging.ILogger<HomePageViewModel> logger,
        ICommandService commandService,
        IProjectService projectService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        MainMenuUtils mainMenuUtils)
    {
        _logger = logger;
        _commandService = commandService;
        _projectService = projectService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _mainMenuUtils = mainMenuUtils;

        PopulateRecentProjects();
    }

    private void PopulateRecentProjects()
    {
        RecentProjects.AddRange(_projectService.GetRecentProjects());
    }

    public List<RecentProject> RecentProjects = new();

    public IAsyncRelayCommand NewProjectCommand => new AsyncRelayCommand(NewProjectCommand_Executed);
    private async Task NewProjectCommand_Executed()
    {
        await _mainMenuUtils.ShowNewProjectDialogAsync();
    }

    public IAsyncRelayCommand NewExampleProjectCommand => new AsyncRelayCommand(NewExampleProjectCommand_Executed);
    private async Task NewExampleProjectCommand_Executed()
    {
        await _mainMenuUtils.ShowNewExampleProjectDialogAsync();
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

