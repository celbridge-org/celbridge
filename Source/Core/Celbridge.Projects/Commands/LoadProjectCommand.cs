using Celbridge.Commands;

namespace Celbridge.Projects.Commands;

internal class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly IProjectLoader _projectLoader;

    public LoadProjectCommand(IProjectLoader projectLoader)
    {
        _projectLoader = projectLoader;
    }

    public string ProjectFilePath { get; set; } = string.Empty;

    public ProjectLoadOrigin Origin { get; set; } = ProjectLoadOrigin.RunningWindow;

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(ProjectFilePath))
        {
            return Result.Fail("Failed to load project because path is empty.");
        }

        return await _projectLoader.LoadProjectAsync(ProjectFilePath, Origin);
    }
}
