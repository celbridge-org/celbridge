using Celbridge.Commands;
using Celbridge.Platform;

namespace Celbridge.Projects.Commands;

public class OpenProjectDataCommand : CommandBase, IOpenProjectDataCommand
{
    private readonly IProjectService _projectService;
    private readonly IFileManagerLauncher _fileManagerLauncher;

    public OpenProjectDataCommand(
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
            return Result.Fail("Failed to open the project data folder because no project is loaded.");
        }

        var dataFolderPath = currentProject.ProjectDataFolderPath;
        var openResult = await _fileManagerLauncher.OpenFileManagerAsync(dataFolderPath);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open the project data folder in the file manager: '{dataFolderPath}'")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }
}
