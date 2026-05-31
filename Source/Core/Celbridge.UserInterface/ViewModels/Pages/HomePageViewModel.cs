using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.FileSystem;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class HomePageViewModel : ObservableObject
{
    private readonly Logging.ILogger<HomePageViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IProjectService _projectService;
    private readonly IFileSystem _fileSystem;
    private readonly MainMenuUtils _mainMenuUtils;

    public HomePageViewModel(
        INavigationService navigationService,
        Logging.ILogger<HomePageViewModel> logger,
        ICommandService commandService,
        IProjectService projectService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        MainMenuUtils mainMenuUtils)
    {
        _logger = logger;
        _commandService = commandService;
        _projectService = projectService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _mainMenuUtils = mainMenuUtils;

        PopulateRecentProjects();
    }

    private void PopulateRecentProjects()
    {
        RecentProjects.AddRange(_projectService.GetRecentProjects());
    }

    public List<RecentProject> RecentProjects = new();

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        await _mainMenuUtils.ShowNewProjectDialogAsync();
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        await _mainMenuUtils.ShowOpenProjectDialogAsync();
    }

    public async Task OpenProjectAsync(string projectFilePath)
    {
        var infoResult = await _fileSystem.GetInfoAsync(projectFilePath);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
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
