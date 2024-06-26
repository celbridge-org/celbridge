using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.BaseLibrary.Commands;
using Celbridge.ProjectAdmin.Services;

namespace Celbridge.ProjectAdmin.Commands;

public class UnloadProjectCommand : CommandBase, IUnloadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly INavigationService _navigationService;
    private readonly IProjectDataService _projectDataService;

    public UnloadProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService,
        IProjectDataService projectDataService)
    {
        _workspaceWrapper = workspaceWrapper;
        _navigationService = navigationService;
        _projectDataService = projectDataService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded && _projectDataService.LoadedProjectData is null)
        {
            // We're already in the desired state so we can early out.
            return Result.Ok();
        }

        return await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectDataService);
    }
}
