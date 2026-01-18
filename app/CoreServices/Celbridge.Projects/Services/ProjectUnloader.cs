using Celbridge.Logging;
using Celbridge.Navigation;
using Celbridge.Workspace;

namespace Celbridge.Projects.Services;

/// <summary>
/// Handles the complete workflow of unloading a project, including workspace page cleanup
/// and navigation orchestration.
/// </summary>
public class ProjectUnloader
{
    private const string EmptyPageTag = "Empty";

    private readonly ILogger<ProjectUnloader> _logger;
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ProjectUnloader(
        ILogger<ProjectUnloader> logger,
        IProjectService projectService,
        INavigationService navigationService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _projectService = projectService;
        _navigationService = navigationService;
        _workspaceWrapper = workspaceWrapper;
    }

    /// <summary>
    /// Unloads the current project, handling workspace page cleanup and navigation.
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

        // Handle workspace page cleanup if it's loaded
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var cleanupResult = await CleanupWorkspacePageAsync();
            if (cleanupResult.IsFailure)
            {
                return cleanupResult;
            }
        }

        // Clear the reference and dispose the project
        _projectService.ClearCurrentProject();
        (currentProject as IDisposable)?.Dispose();

        _logger.LogInformation("Project '{ProjectName}' unloaded successfully", projectName);
        return Result.Ok();
    }

    private async Task<Result> CleanupWorkspacePageAsync()
    {
        // The logic here is complicated because we need to teardown the workspace in the
        // unloaded callback of the Workspace Page, and there are multiple code paths to consider.

        // The workspace page uses NavigationCacheMode.Required, so it stays loaded in memory
        // even when the user navigates to other pages like Home or Community.
        // We need to ensure proper cleanup of the workspace page.

        // Navigate to the Workspace page first to make it the active/visible page.
        // This is necessary because OnNavigatingFrom (which handles cleanup) only runs
        // when navigating away from the currently visible page.
        var navResult = _navigationService.NavigateToPage(NavigationConstants.WorkspaceTag);
        if (navResult.IsFailure)
        {
            return Result.Fail("Failed to navigate to workspace page for cleanup")
                .WithErrors(navResult);
        }

        // Give the UI enough time to complete the navigation and render.
        // This ensures the Workspace page is fully active before we navigate away.
        await Task.Delay(100);

        // Signal that the workspace page should perform cleanup when it unloads.
        // This must be called AFTER navigating to the Workspace page, because
        // NavigateToPage clears the cleanup flag after each navigation.
        _navigationService.RequestWorkspacePageCleanup();

        // Now navigate to the empty page to trigger OnNavigatingFrom on the WorkspacePage,
        // which will disable caching and allow proper cleanup on unload.
        navResult = _navigationService.NavigateToPage(EmptyPageTag);
        if (navResult.IsFailure)
        {
            return Result.Fail("Failed to navigate to empty page for workspace cleanup")
                .WithErrors(navResult);
        }

        // Wait until the workspace is fully unloaded
        while (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            await Task.Delay(50);
        }

        return Result.Ok();
    }
}
