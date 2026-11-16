using Celbridge.Commands;

namespace Celbridge.Projects.Commands;

public class TrustProjectCommand : CommandBase, ITrustProjectCommand
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    public TrustProjectCommand(
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
            return Result.Fail("Failed to trust the project because no project is loaded.");
        }

        var trustResult = await _projectService.TrustProjectAsync(currentProject.ProjectFilePath);
        if (trustResult.IsFailure)
        {
            return Result.Fail($"Failed to trust the project: '{currentProject.ProjectFilePath}'")
                .WithErrors(trustResult);
        }

        _commandService.Execute<IReloadProjectCommand>();

        return Result.Ok();
    }
}
