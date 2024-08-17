using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.ProjectAdmin.Services;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.ProjectAdmin.Commands;

public class UnloadProjectCommand : CommandBase, IUnloadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly INavigationService _navigationService;
    private readonly IProjectService _projectService;

    public UnloadProjectCommand(
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService,
        IProjectService projectService)
    {
        _workspaceWrapper = workspaceWrapper;
        _navigationService = navigationService;
        _projectService = projectService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded && _projectService.LoadedProject is null)
        {
            // We're already in the desired state so we can early out.
            return Result.Ok();
        }

        return await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectService);
    }

    //
    // Static methods for scripting support.
    //

    public static void UnloadProject()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUnloadProjectCommand>();
    }
}
