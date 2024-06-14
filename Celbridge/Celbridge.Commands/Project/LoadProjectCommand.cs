using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Commands.Utils;
using Celbridge.BaseLibrary.Settings;

namespace Celbridge.Commands.Project;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;

    public LoadProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        INavigationService navigationService,
        IEditorSettings editorSettings)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _navigationService = navigationService;
        _editorSettings = editorSettings;
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
            // Todo: Show an error alert to the user
            _editorSettings.PreviousLoadedProject = string.Empty;
            return Result.Fail($"Failed to load project: {ProjectPath}");
        }

        _editorSettings.PreviousLoadedProject = ProjectPath;

        return Result.Ok();
    }
}
