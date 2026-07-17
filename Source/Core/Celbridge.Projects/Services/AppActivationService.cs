using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.UserInterface;

namespace Celbridge.Projects.Services;

public class AppActivationService : IAppActivationService
{
    private readonly ILogger<AppActivationService> _logger;
    private readonly ICommandService _commandService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalFileSystem _fileSystem;
    private readonly INavigationService _navigationService;

    // All access is on the UI thread: the macOS open-document callback, the Windows activation call, and the
    // MainPageLoadedMessage handler all run there.
    private bool _startupCompleted;
    private string? _pendingProjectFilePath;

    public AppActivationService(
        ILogger<AppActivationService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        ISettingsService settingsService,
        ILocalFileSystem fileSystem,
        INavigationService navigationService)
    {
        _logger = logger;
        _commandService = commandService;
        _settingsService = settingsService;
        _fileSystem = fileSystem;
        _navigationService = navigationService;

        messengerService.Register<MainPageLoadedMessage>(this, OnMainPageLoaded);
    }

    public void OnFilesActivated(IReadOnlyList<string> filePaths)
    {
        var projectFilePath = filePaths.FirstOrDefault(IsProjectFile);
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        if (_startupCompleted)
        {
            LoadProject(projectFilePath);
        }
        else
        {
            // The startup flow has not run yet, so defer the project to it rather than racing a second load
            // against the previous-project open.
            _pendingProjectFilePath = projectFilePath;
        }
    }

    private async void OnMainPageLoaded(object recipient, MainPageLoadedMessage message)
    {
        if (_startupCompleted)
        {
            return;
        }

        try
        {
            var projectFilePath = await ChooseStartupProjectAsync();
            _startupCompleted = true;

            // A file activation that arrived while choosing wins over the startup choice.
            if (_pendingProjectFilePath != null)
            {
                projectFilePath = _pendingProjectFilePath;
                _pendingProjectFilePath = null;
            }

            if (!string.IsNullOrEmpty(projectFilePath))
            {
                LoadProject(projectFilePath);
            }
            else
            {
                // No project to open, so show the home page.
                _navigationService.NavigateToPage(NavigationConstants.HomeTag);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to open a project at startup");
        }
    }

    // Prefer a project file the OS asked the app to open at launch, falling back to the project that was open
    // last time the app ran.
    private async Task<string?> ChooseStartupProjectAsync()
    {
        var activationProjectFile = _pendingProjectFilePath;
        _pendingProjectFilePath = null;
        if (!string.IsNullOrEmpty(activationProjectFile) &&
            await ProjectFileExistsAsync(activationProjectFile))
        {
            return activationProjectFile;
        }

        var previousProjectFile = _settingsService.Get(SettingCatalog.Project.PreviousProject);
        if (!string.IsNullOrEmpty(previousProjectFile) &&
            await ProjectFileExistsAsync(previousProjectFile))
        {
            return previousProjectFile;
        }

        return null;
    }

    private async Task<bool> ProjectFileExistsAsync(string filePath)
    {
        var infoResult = await _fileSystem.GetInfoAsync(filePath);
        return infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
    }

    private void LoadProject(string projectFilePath)
    {
        _logger.LogDebug($"Opening project file: {projectFilePath}");

        _commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = projectFilePath;
        });
    }

    private static bool IsProjectFile(string filePath)
    {
        var extension = System.IO.Path.GetExtension(filePath);
        return string.Equals(extension, ProjectConstants.ProjectFileExtension, StringComparison.OrdinalIgnoreCase);
    }
}
