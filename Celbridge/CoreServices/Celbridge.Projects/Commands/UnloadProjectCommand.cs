using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.Workspace;

namespace Celbridge.Projects.Commands;

public class UnloadProjectCommand : CommandBase, IUnloadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly INavigationService _navigationService;
    private readonly IProjectService _projectService;

    public UnloadProjectCommand(
        INavigationService navigationService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _navigationService = navigationService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded && _projectService.CurrentProject is null)
        {
            // We're already in the desired state so we can early out.
            return Result.Ok();
        }

        return await _projectService.UnloadProjectAsync();
    }

    //
    // Static methods for scripting support.
    //

    public static void UnloadProject()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IUnloadProjectCommand>();
    }
}
