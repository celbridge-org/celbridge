using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Navigation;
using Celbridge.ProjectAdmin.Services;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectAdmin.Commands;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private const string HomePageName = "HomePage";

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public LoadProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        INavigationService navigationService,
        IEditorSettings editorSettings,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _navigationService = navigationService;
        _editorSettings = editorSettings;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public string ProjectFilePath { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(ProjectFilePath))
        {
            return Result.Fail("Failed to load project because path is empty.");
        }

        if (_projectService.LoadedProject?.ProjectFilePath == ProjectFilePath)
        {
            // The project is already loaded.
            // We can just early out here as we're already in the expected end state.
            return Result.Ok();
        }

        // Close any loaded project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectService);

        // Load the project
        var loadResult = await ProjectUtils.LoadProjectAsync(_workspaceWrapper, _navigationService, _projectService, ProjectFilePath);

        if (loadResult.IsFailure)
        {
            _editorSettings.PreviousLoadedProject = string.Empty;

            var titleString = _stringLocalizer.GetString("LoadProjectFailedAlert_Title");
            var messageString = _stringLocalizer.GetString("LoadProjectFailedAlert_Message", ProjectFilePath);

            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // Return to the home page so the user can decide what to do next
            _navigationService.NavigateToPage(HomePageName);

            return Result.Fail($"Failed to load project file: {ProjectFilePath}. {loadResult.Error}.");
        }

        _editorSettings.PreviousLoadedProject = ProjectFilePath;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void LoadProject(string projectFilePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ILoadProjectCommand>(command => command.ProjectFilePath = projectFilePath);
    }
}
