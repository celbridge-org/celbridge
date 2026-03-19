using Celbridge.Commands;

namespace Celbridge.Projects.Commands;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IProjectLoader _projectLoader;

    public LoadProjectCommand(
        ICommandService commandService,
        IProjectService projectService,
        IProjectLoader projectLoader)
    {
        _commandService = commandService;
        _projectService = projectService;
        _projectLoader = projectLoader;
    }

    public string ProjectFilePath { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(ProjectFilePath))
        {
            return Result.Fail("Failed to load project because path is empty.");
        }

        if (_projectService.CurrentProject?.ProjectFilePath == ProjectFilePath)
        {
            // The project is already loaded - we can just early out.
            return Result.Ok();
        }

        // Close any loaded project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Delegate to ProjectLoader for the complete loading workflow
        return await _projectLoader.LoadProjectAsync(ProjectFilePath);
    }

    //
    // Static methods for scripting support.
    //

    public static void LoadProject(string projectFilePath)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = projectFilePath;
        });
    }
}
