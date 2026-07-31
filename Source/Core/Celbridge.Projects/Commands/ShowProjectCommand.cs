using Celbridge.Commands;
using Celbridge.Platform;

namespace Celbridge.Projects.Commands;

public class ShowProjectCommand : CommandBase, IShowProjectCommand
{
    private readonly IProjectService _projectService;
    private readonly IFileManagerLauncher _fileManagerLauncher;

    public ShowProjectCommand(
        IProjectService projectService,
        IFileManagerLauncher fileManagerLauncher)
    {
        _projectService = projectService;
        _fileManagerLauncher = fileManagerLauncher;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            // No project is open, so there is nothing to reveal.
            return Result.Ok();
        }

        string projectFilePath = currentProject.ProjectFilePath;

        var openResult = await _fileManagerLauncher.OpenFileManagerAsync(projectFilePath);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to reveal the project file in the file manager: '{projectFilePath}'")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }
}
