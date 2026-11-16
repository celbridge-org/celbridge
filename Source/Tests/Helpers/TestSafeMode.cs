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
    /// failed to load. The project is trusted.
    /// </summary>
    public static ISafeModeService Create(bool isActive)
    {
        return Create(isTrusted: true, configIsHealthy: !isActive);
    }

    /// <summary>
    /// Returns a Safe Mode service for a project with the given trust and configuration.
    /// </summary>
    public static ISafeModeService Create(bool isTrusted, bool configIsHealthy)
    {
        var project = Substitute.For<IProject>();
        project.IsTrusted.Returns(isTrusted);
        project.ConfigIsHealthy.Returns(configIsHealthy);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new SafeModeService(projectService);
    }
}
