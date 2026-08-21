using Celbridge.Logging;
using Celbridge.Server;
using Celbridge.UserInterface;

namespace Celbridge.Projects.Services;

/// <summary>
/// Handles the complete workflow of unloading a project, including tearing the workspace down.
/// </summary>
public class ProjectUnloader
{
    private readonly ILogger<ProjectUnloader> _logger;
    private readonly IProjectService _projectService;
    private readonly IApplicationShell _applicationShell;
    private readonly IServerService _serverService;
    private readonly IProjectHealthService _projectHealthService;

    public ProjectUnloader(
        ILogger<ProjectUnloader> logger,
        IProjectService projectService,
        IApplicationShell applicationShell,
        IServerService serverService,
        IProjectHealthService projectHealthService)
    {
        _logger = logger;
        _projectService = projectService;
        _applicationShell = applicationShell;
        _serverService = serverService;
        _projectHealthService = projectHealthService;
    }

    /// <summary>
    /// Unloads the current project, tearing the workspace down before the project it belongs to goes away.
    /// </summary>
    public async Task<Result> UnloadProjectAsync()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            // No project loaded - nothing to do
            return Result.Ok();
        }

        var projectName = currentProject.ProjectName;
        _logger.LogInformation("Unloading project '{ProjectName}'", projectName);

        // The shell destroys the workspace view rather than caching it, so this completes once the
        // workspace has finished tearing down.
        var closeResult = await _applicationShell.CloseWorkspaceAsync();
        if (closeResult.IsFailure)
        {
            return Result.Fail($"Failed to close the workspace for project '{projectName}'")
                .WithErrors(closeResult);
        }

        // Health describes the load that is ending, so it goes with the project rather than lingering
        // on the switcher while no project is open.
        _projectHealthService.ClearHealth();

        // Clear the reference and dispose the project
        _projectService.ClearCurrentProject();
        (currentProject as IDisposable)?.Dispose();

        // Stop the server. The assigned port is retained inside ServerService
        // so the next StartAsync call binds to the same port.
        await _serverService.StopAsync();

        _logger.LogInformation("Project '{ProjectName}' unloaded successfully", projectName);
        return Result.Ok();
    }
}
