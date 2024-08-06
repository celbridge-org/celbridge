using Celbridge.Resources;
using Celbridge.Navigation;
using Celbridge.Workspace;
using Celbridge.Commands;
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
        if (!_workspaceWrapper.IsWorkspacePageLoaded && _projectDataService.LoadedProjectData is null)
        {
            // We're already in the desired state so we can early out.
            return Result.Ok();
        }

        return await ProjectUtils.UnloadProjectAsync(_workspaceWrapper, _navigationService, _projectDataService);
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
