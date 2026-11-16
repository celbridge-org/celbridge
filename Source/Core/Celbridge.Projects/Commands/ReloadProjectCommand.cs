using Celbridge.Commands;

namespace Celbridge.Projects.Commands;

public class ReloadProjectCommand : CommandBase, IReloadProjectCommand
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    public ReloadProjectCommand(
        ICommandService commandService,
        IProjectService projectService)
    {
        _commandService = commandService;
        _projectService = projectService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return Result.Fail("Failed to reload the project because no project is loaded.");
        }

        string projectFilePath = currentProject.ProjectFilePath;

        // Unload immediately so the subsequent load does not early out on the already-loaded project.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        _commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = projectFilePath;
        });

        return Result.Ok();
    }
}
