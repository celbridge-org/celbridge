using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.UserInterface;

namespace Celbridge.Projects.Services;

public class AppActivationService : IAppActivationService
{
    // The project the startup flow opens, and how the launch came to open it.
    private sealed record StartupProject(string ProjectFilePath, ProjectLoadOrigin Origin);

    private readonly ILogger<AppActivationService> _logger;
    private readonly ICommandService _commandService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalFileSystem _fileSystem;

    // All access is on the UI thread: the macOS open-document callback, the Windows activation call, and the
    // MainPageLoadedMessage handler all run there.
    private bool _startupCompleted;
    private string? _pendingProjectFilePath;

    public AppActivationService(
        ILogger<AppActivationService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        ISettingsService settingsService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _commandService = commandService;
        _settingsService = settingsService;
        _fileSystem = fileSystem;

        messengerService.Register<MainPageLoadedMessage>(this, OnMainPageLoaded);
    }

    public void OnFilesActivated(IReadOnlyList<string> filePaths)
    {
        var projectFilePath = filePaths.FirstOrDefault(ProjectFileHelper.IsProjectFile);
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        if (_startupCompleted)
        {
            LoadProject(projectFilePath, ProjectLoadOrigin.RunningWindow);
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
            var startupProject = await ChooseStartupProjectAsync();
            _startupCompleted = true;

            // A file activation that arrived while choosing wins over the startup choice.
            if (_pendingProjectFilePath != null)
            {
                startupProject = new StartupProject(_pendingProjectFilePath, ProjectLoadOrigin.LaunchFile);
                _pendingProjectFilePath = null;
            }

            // With no project to open, the shell keeps showing Home.
            if (startupProject is not null)
            {
                LoadProject(startupProject.ProjectFilePath, startupProject.Origin);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to open a project at startup");
        }
    }

    // Prefer a project file the OS asked the app to open at launch, falling back to the project that was open
    // last time the app ran.
    private async Task<StartupProject?> ChooseStartupProjectAsync()
    {
        var activationProjectFile = _pendingProjectFilePath;
        _pendingProjectFilePath = null;
        if (!string.IsNullOrEmpty(activationProjectFile) &&
            await ProjectFileExistsAsync(activationProjectFile))
        {
            return new StartupProject(activationProjectFile, ProjectLoadOrigin.LaunchFile);
        }

        var previousProjectFile = _settingsService.Get(SettingCatalog.Project.PreviousProject);
        if (!string.IsNullOrEmpty(previousProjectFile) &&
            await ProjectFileExistsAsync(previousProjectFile))
        {
            return new StartupProject(previousProjectFile, ProjectLoadOrigin.PreviousProject);
        }

        return null;
    }

    private async Task<bool> ProjectFileExistsAsync(string filePath)
    {
        var infoResult = await _fileSystem.GetInfoAsync(filePath);
        return infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
    }

    private void LoadProject(string projectFilePath, ProjectLoadOrigin origin)
    {
        _logger.LogDebug($"Opening project file: {projectFilePath}");

        _commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = projectFilePath;
            command.Origin = origin;
        });
    }
}
