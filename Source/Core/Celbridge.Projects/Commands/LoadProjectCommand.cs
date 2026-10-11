using Celbridge.Automation;
using Celbridge.Commands;

namespace Celbridge.Projects.Commands;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IProjectLoader _projectLoader;
    private readonly IAutomationService _automationService;

    public LoadProjectCommand(
        ICommandService commandService,
        IProjectService projectService,
        IProjectLoader projectLoader,
        IAutomationService automationService)
    {
        _commandService = commandService;
        _projectService = projectService;
        _projectLoader = projectLoader;
        _automationService = automationService;
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
        var loadResult = await _projectLoader.LoadProjectAsync(ProjectFilePath);
        if (loadResult.IsFailure)
        {
            _automationService.LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
        }

        return loadResult;
    }
}
