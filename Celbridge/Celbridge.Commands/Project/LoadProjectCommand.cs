using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Commands.Utils;

namespace Celbridge.Commands.Project;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly INavigationService _navigationService;

    public LoadProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        INavigationService navigationService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _navigationService = navigationService;
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

        // Close any open project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectDataService);

        // Load the project
        return ProjectUtils.LoadProject(_navigationService, _projectDataService, ProjectPath);
    }
}
