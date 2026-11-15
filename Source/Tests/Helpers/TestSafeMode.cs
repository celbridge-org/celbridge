using Celbridge.Projects;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.Helpers;

/// <summary>
/// Builds a real Safe Mode service for a test's workspace. The service has the application's allow list.
/// </summary>
public static class TestSafeMode
{
    /// <summary>
    /// Returns a Safe Mode service that is on when isActive is true. Safe Mode is on because the project file
    /// failed to load.
    /// </summary>
    public static ISafeModeService Create(bool isActive)
    {
        var project = Substitute.For<IProject>();
        project.ConfigIsHealthy.Returns(!isActive);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new SafeModeService(projectService);
    }
}
