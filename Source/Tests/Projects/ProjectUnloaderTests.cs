using Celbridge.Notifications;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Server;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers ProjectUnloader's workspace teardown: the successful unload, the early-out when no project is
/// loaded, and the teardown failure, which is reported without stopping the unload it is part of. Every
/// notification describes the project being unloaded, so each path that unloads also clears them.
/// </summary>
[TestFixture]
public class ProjectUnloaderTests
{
    private IProjectService _projectService = null!;
    private IApplicationShell _applicationShell = null!;
    private IServerService _serverService = null!;
    private INotificationCentre _notificationCentre = null!;
    private ProjectUnloader _projectUnloader = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
        _applicationShell = Substitute.For<IApplicationShell>();
        _serverService = Substitute.For<IServerService>();
        _notificationCentre = Substitute.For<INotificationCentre>();

        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("TestProject");
        _projectService.CurrentProject.Returns(project);

        _applicationShell.CloseWorkspaceAsync().Returns(Result.Ok());

        _projectUnloader = new ProjectUnloader(
            new NullLogger<ProjectUnloader>(),
            _projectService,
            _applicationShell,
            _serverService,
            _notificationCentre);
    }

    [Test]
    public async Task UnloadProject_WithNoProjectLoaded_Succeeds()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        var result = await _projectUnloader.UnloadProjectAsync();

        Assert.That(result.IsSuccess, Is.True);
        await _applicationShell.DidNotReceive().CloseWorkspaceAsync();
        _notificationCentre.DidNotReceive().Clear();
    }

    [Test]
    public async Task UnloadProject_WhenWorkspaceCloses_Succeeds()
    {
        var result = await _projectUnloader.UnloadProjectAsync();

        Assert.That(result.IsSuccess, Is.True);
        await _applicationShell.Received(1).CloseWorkspaceAsync();
        _notificationCentre.Received(1).Clear();
        _projectService.Received(1).ClearCurrentProject();
        await _serverService.Received(1).StopAsync();
    }

    [Test]
    public async Task UnloadProject_WhenWorkspaceFailsToClose_ReportsTheFailureAndStillUnloads()
    {
        _applicationShell.CloseWorkspaceAsync().Returns(Result.Fail("Teardown failed"));

        var result = await _projectUnloader.UnloadProjectAsync();

        Assert.That(result.IsFailure, Is.True);

        // The shell takes the view down whether the teardown succeeded or not, so stopping here would leave
        // the project current with no workspace on screen.
        _notificationCentre.Received(1).Clear();
        _projectService.Received(1).ClearCurrentProject();
        await _serverService.Received(1).StopAsync();
    }
}
