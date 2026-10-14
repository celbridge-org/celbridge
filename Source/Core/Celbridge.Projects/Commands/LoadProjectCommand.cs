using Celbridge.Automation;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Settings;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Commands;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly ILogger<LoadProjectCommand> _logger;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IProjectLoader _projectLoader;
    private readonly IAutomationService _automationService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IStringLocalizer _stringLocalizer;

    public LoadProjectCommand(
        ILogger<LoadProjectCommand> logger,
        ICommandService commandService,
        IProjectService projectService,
        IProjectLoader projectLoader,
        IAutomationService automationService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _commandService = commandService;
        _projectService = projectService;
        _projectLoader = projectLoader;
        _automationService = automationService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _stringLocalizer = stringLocalizer;
    }

    public string ProjectFilePath { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(ProjectFilePath))
        {
            return Result.Fail("Failed to load project because path is empty.");
        }

        if (_projectService.CurrentProject?.ProjectFilePath == ProjectFilePath)
        {
            // The project is already loaded - we can just early out.
            return Result.Ok();
        }

        // A folder contains at most one project file. The check runs before the current project closes, so a
        // refused project leaves the current project open.
        var otherProjectFilePath = await FindOtherProjectFileAsync();
        if (otherProjectFilePath is not null)
        {
            return await RefuseProjectAsync(otherProjectFilePath);
        }

        // Close any loaded project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Delegate to ProjectLoader for the complete loading workflow
        Result loadResult;
        try
        {
            loadResult = await _projectLoader.LoadProjectAsync(ProjectFilePath);
        }
        catch (Exception ex)
        {
            loadResult = Result.Fail($"Failed to load project: '{ProjectFilePath}'")
                .WithException(ex);
        }

        if (loadResult.IsFailure)
        {
            _automationService.LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
        }

        return loadResult;
    }

    // Returns the path of a project file in the project's folder other than the one being opened, or null if
    // there is none.
    private async Task<string?> FindOtherProjectFileAsync()
    {
        var projectFolderPath = ProjectFileHelper.GetProjectFolderPath(ProjectFilePath);
        var projectFilesResult = await _projectService.GetProjectFilesAsync(projectFolderPath);
        if (projectFilesResult.IsFailure)
        {
            // The load goes ahead, and the loader reports any problem reading the folder.
            _logger.LogWarning(projectFilesResult, $"Failed to check the project folder for other project files: '{projectFolderPath}'");
            return null;
        }

        var projectFileName = Path.GetFileName(ProjectFilePath);
        foreach (var projectFilePath in projectFilesResult.Value)
        {
            var fileName = Path.GetFileName(projectFilePath);
            if (!string.Equals(fileName, projectFileName, PathComparison.Comparison))
            {
                return projectFilePath;
            }
        }

        return null;
    }

    private async Task<Result> RefuseProjectAsync(string otherProjectFilePath)
    {
        // The project can't open until the other project file is moved or deleted, so the next launch skips it.
        var previousProject = _settingsService.Get(SettingCatalog.Project.PreviousProject);
        if (string.Equals(previousProject, ProjectFilePath, PathComparison.Comparison))
        {
            _settingsService.Set(SettingCatalog.Project.PreviousProject, string.Empty);
        }

        var otherFileName = Path.GetFileName(otherProjectFilePath);
        var title = _stringLocalizer.GetString("AnotherProjectFileAlert_Title");
        var message = _stringLocalizer.GetString("AnotherProjectFileAlert_Message", otherFileName);
        await _dialogService.ShowAlertDialogAsync(title, message);

        _automationService.LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));

        return Result.Fail($"Failed to load project because its folder contains another project file: '{otherProjectFilePath}'");
    }
}
