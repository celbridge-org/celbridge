using Celbridge.Commands;
using Celbridge.Projects.Services;
using Celbridge.Workspace;

namespace Celbridge.Projects.Commands;

public class UnloadProjectCommand : CommandBase, IUnloadProjectCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ProjectUnloader _projectUnloader;

    public UnloadProjectCommand(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        ProjectUnloader projectUnloader)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _projectUnloader = projectUnloader;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded && _projectService.CurrentProject is null)
        {
            // We're already in the desired state so we can early out.
            return Result.Ok();
        }

        return await _projectUnloader.UnloadProjectAsync();
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
