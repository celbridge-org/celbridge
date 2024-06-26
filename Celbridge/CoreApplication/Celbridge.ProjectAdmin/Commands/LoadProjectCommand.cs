using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.BaseLibrary.Settings;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Commands;
using Celbridge.ProjectAdmin.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectAdmin.Commands;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public LoadProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        INavigationService navigationService,
        IEditorSettings editorSettings,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _navigationService = navigationService;
        _editorSettings = editorSettings;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public string ProjectPath { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(ProjectPath))
        {
            return Result.Fail("Failed to load project because path is empty.");
        }

        if (_projectDataService.LoadedProjectData?.ProjectFilePath == ProjectPath)
        {
            // The project is already loaded.
            // We can just early out here as we're already in the expected end state.
            return Result.Ok();
        }

        // Close any loaded project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectDataService);

        // Load the project
        var loadResult = await ProjectUtils.LoadProjectAsync(_workspaceWrapper, _navigationService, _projectDataService, ProjectPath);

        if (loadResult.IsFailure)
        {
            _editorSettings.PreviousLoadedProject = string.Empty;

            var titleString = _stringLocalizer.GetString("LoadProjectFailedAlert_Title");
            var bodyString = _stringLocalizer.GetString("LoadProjectFailedAlert_Body", ProjectPath);
            var okString = _stringLocalizer.GetString("DialogButton_Ok");

            await _dialogService.ShowAlertDialogAsync(titleString, bodyString, okString);

            return Result.Fail($"Failed to load project: {ProjectPath}. {loadResult.Error}.");
        }

        _editorSettings.PreviousLoadedProject = ProjectPath;

        return Result.Ok();
    }

    public static void LoadProject(string projectPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ILoadProjectCommand>(command => command.ProjectPath = projectPath);
    }
}
