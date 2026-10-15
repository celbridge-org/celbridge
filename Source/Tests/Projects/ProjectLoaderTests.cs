using Celbridge.Automation;
using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Notifications;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers ProjectLoader's workflow. Each test starts with another project open in this instance. The checks that
/// can refuse a project run before that project closes: a project already open here, a second project file in the
/// folder, the lock, the version and the upgrade prompt. A lock that another instance holds has a different outcome
/// for each way a load can start. The tests also cover when the lock is kept and released, and the alerts a refused
/// version raises.
/// </summary>
[TestFixture]
public class ProjectLoaderTests
{
    private const string ProjectFilePath = "/projects/Acme/Acme.celbridge";
    private const string OtherProjectFilePath = "/projects/Acme/Other.celbridge";
    private const string CurrentProjectFilePath = "/projects/Current/Current.celbridge";

    private IProjectMigrationService _migrationService = null!;
    private IProjectService _projectService = null!;
    private FakeProjectLockService _projectLockService = null!;
    private IDialogService _dialogService = null!;
    private IApplicationShell _applicationShell = null!;
    private ISettingsService _settingsService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IAutomationService _automationService = null!;
    private IProjectLoadReporter _loadReporter = null!;
    private ProjectLoader _projectLoader = null!;

    [SetUp]
    public void Setup()
    {
        _migrationService = Substitute.For<IProjectMigrationService>();
        _projectService = Substitute.For<IProjectService>();
        _projectLockService = new FakeProjectLockService();
        _dialogService = Substitute.For<IDialogService>();
        _applicationShell = Substitute.For<IApplicationShell>();
        _settingsService = Substitute.For<ISettingsService>();
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _automationService = Substitute.For<IAutomationService>();
        _loadReporter = Substitute.For<IProjectLoadReporter>();

        // By default the project is current, and its workspace loads.
        _migrationService.CheckMigrationAsync(ProjectFilePath).Returns(MigrationResult.Success());
        StubFolderProjectFiles(ProjectFilePath);
        _projectService.LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>())
            .Returns(Result<IProject>.Ok(Substitute.For<IProject>()));
        _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>()).Returns(Result.Ok());
        _applicationShell.CloseWorkspaceAsync().Returns(Result.Ok());
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);

        // Another project is open in this instance, with its lock held.
        var currentProject = Substitute.For<IProject>();
        currentProject.ProjectName.Returns("Current");
        currentProject.ProjectFilePath.Returns(CurrentProjectFilePath);
        _projectService.CurrentProject.Returns(currentProject);
        _projectLockService.HeldProjectFiles.Add(CurrentProjectFilePath);

        var projectUnloader = new ProjectUnloader(
            new NullLogger<ProjectUnloader>(),
            _projectService,
            _applicationShell,
            Substitute.For<IServerService>(),
            Substitute.For<INotificationCentre>(),
            _projectLockService);

        _projectLoader = new ProjectLoader(
            new NullLogger<ProjectLoader>(),
            _migrationService,
            _projectService,
            _projectLockService,
            projectUnloader,
            _dialogService,
            _applicationShell,
            _settingsService,
            _workspaceWrapper,
            Substitute.For<IMessengerService>(),
            _automationService,
            CreateStringLocalizer(),
            _loadReporter);
    }

    [Test]
    public async Task AProjectThatOpens_ClosesTheCurrentProjectAndKeepsItsOwnLock()
    {
        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        _projectService.Received(1).ClearCurrentProject();
        _projectLockService.HeldProjectFiles.Should().Equal(ProjectFilePath);
        _settingsService.Received(1).Set(SettingCatalog.Project.PreviousProject, ProjectFilePath);
        _automationService.DidNotReceive().LogEvent(Arg.Any<ProjectLoadFailedEvent>());
    }

    [Test]
    public async Task TheLock_IsTakenBeforeTheCurrentProjectCloses()
    {
        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        // The current project's lock is released as the last step of closing it.
        _projectLockService.Calls.Should().Equal($"take {ProjectFilePath}", $"release {CurrentProjectFilePath}");
    }

    [Test]
    public async Task AProjectThisInstanceHasOpen_IsNotLoadedAgain()
    {
        _projectLockService.HeldProjectFiles.Add(ProjectFilePath);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _migrationService.DidNotReceiveWithAnyArgs().CheckMigrationAsync(default!);
        _projectService.DidNotReceive().ClearCurrentProject();
    }

    [Test]
    public async Task ALoadThatFails_ReportsTheFailureAndReleasesTheLock()
    {
        _projectService.LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>())
            .Returns(Result<IProject>.Fail("The load failed"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        _projectLockService.HeldProjectFiles.Should().NotContain(ProjectFilePath);
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task ALoadThatThrows_FailsAndReleasesTheLock()
    {
        _migrationService.CheckMigrationAsync(ProjectFilePath)
            .Returns<Task<MigrationResult>>(_ => throw new InvalidOperationException("The check threw"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        _projectLockService.HeldProjectFiles.Should().NotContain(ProjectFilePath);
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
        await _loadReporter.Received(1).FlushAsync();
    }

    [Test]
    public async Task ALockThatCannotBeTaken_LoadsTheProjectWithoutIt()
    {
        _projectLockService.TakeResult = Result<bool>.Fail("The data folder could not be created");

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>());
    }

    [Test]
    public async Task ANewerVersion_AlertNamesTheVersionToInstall()
    {
        StubMigration(MigrationStatus.NewerCelbridgeVersion, "1.4.0", "1.3.0");

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();

        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Newer Version Required",
            Arg.Is<string>(message =>
                message.Contains("\"Acme\"") &&
                message.Contains("v1.4.0 or later") &&
                message.Contains("(v1.3.0)")));

        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
    }

    [Test]
    public async Task AnUnsupportedVersion_AlertNamesTheOldestSupportedVersion()
    {
        StubMigration(MigrationStatus.UnsupportedCelbridgeVersion, "0.3.0", "1.3.0");

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();

        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Project Not Supported",
            Arg.Is<string>(message =>
                message.Contains("\"Acme\"") &&
                message.Contains("v0.3.0") &&
                message.Contains($"before v{ProjectConstants.MinimumSupportedCelbridgeVersion}")));

        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
    }

    [Test]
    public async Task ANewerVersion_LeavesTheCurrentProjectOpenAndReleasesTheLock()
    {
        StubMigration(MigrationStatus.NewerCelbridgeVersion, "1.4.0", "1.3.0");

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        AssertTheCurrentProjectStaysOpen();
        _projectLockService.HeldProjectFiles.Should().NotContain(ProjectFilePath);
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task ANewerVersion_ClearsThePreviousProjectWhenItNamesThisProject()
    {
        StubMigration(MigrationStatus.NewerCelbridgeVersion, "1.4.0", "1.3.0");
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(ProjectFilePath);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        _settingsService.Received(1).Set(SettingCatalog.Project.PreviousProject, string.Empty);
    }

    [Test]
    public async Task ANewerVersion_KeepsThePreviousProjectWhenItNamesTheOpenProject()
    {
        StubMigration(MigrationStatus.NewerCelbridgeVersion, "1.4.0", "1.3.0");
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(CurrentProjectFilePath);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        _settingsService.DidNotReceiveWithAnyArgs().Set(SettingCatalog.Project.PreviousProject, default!);
    }

    [Test]
    public async Task ADeclinedUpgrade_LeavesTheCurrentProjectOpenAndReleasesTheLock()
    {
        StubMigration(MigrationStatus.UpgradeRequired, "1.2.0", "1.3.0");
        StubUpgradeAnswer(false);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.HeldProjectFiles.Should().NotContain(ProjectFilePath);
        await _migrationService.DidNotReceiveWithAnyArgs().PerformMigrationUpgradeAsync(default!);
        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
        _automationService.DidNotReceive().LogEvent(Arg.Any<ProjectLoadFailedEvent>());
    }

    [Test]
    public async Task AConfirmedUpgrade_ClosesTheCurrentProjectBeforeUpgrading()
    {
        StubMigration(MigrationStatus.UpgradeRequired, "1.2.0", "1.3.0");
        StubUpgradeAnswer(true);
        _migrationService.PerformMigrationUpgradeAsync(ProjectFilePath).Returns(MigrationResult.Success());

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _projectService.ClearCurrentProject();
            _migrationService.PerformMigrationUpgradeAsync(ProjectFilePath);
        });
    }

    [Test]
    public async Task AnotherProjectFileInTheFolder_NamesItAndLeavesTheCurrentProjectOpen()
    {
        StubFolderProjectFiles(OtherProjectFilePath, ProjectFilePath);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Another Project File Found",
            Arg.Is<string>(message => message.Contains("\"Other.celbridge\"")));
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.Calls.Should().BeEmpty();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task AnotherProjectFileInTheFolder_ClearsThePreviousProjectWhenItNamesThisProject()
    {
        StubFolderProjectFiles(OtherProjectFilePath, ProjectFilePath);
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(ProjectFilePath);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        _settingsService.Received(1).Set(SettingCatalog.Project.PreviousProject, string.Empty);
    }

    [Test]
    public async Task AnotherProjectFileInTheFolder_KeepsThePreviousProjectWhenItNamesTheOpenProject()
    {
        StubFolderProjectFiles(OtherProjectFilePath, ProjectFilePath);
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(CurrentProjectFilePath);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        _settingsService.DidNotReceiveWithAnyArgs().Set(SettingCatalog.Project.PreviousProject, default!);
    }

    [Test]
    public async Task AProjectOpenInAnotherInstance_FromARunningWindow_ShowsTheAlertAndLeavesTheCurrentProjectOpen()
    {
        _projectLockService.TakeResult = false;

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Project Already Open",
            Arg.Is<string>(message => message.Contains("\"Acme\"")));
        AssertTheCurrentProjectStaysOpen();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
        await _applicationShell.DidNotReceive().ExitApplicationAsync();
    }

    [Test]
    public async Task AProjectOpenInAnotherInstance_FromTheLaunchFile_ShowsTheAlertAndThenExits()
    {
        _projectLockService.TakeResult = false;

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.LaunchFile);

        result.IsFailure.Should().BeTrue();
        Received.InOrder(() =>
        {
            _dialogService.ShowAlertDialogAsync("Project Already Open", Arg.Any<string>());
            _applicationShell.ExitApplicationAsync();
        });
        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
    }

    [Test]
    public async Task AProjectOpenInAnotherInstance_AsThePreviousProject_ShowsHomeWithoutAnAlert()
    {
        _projectLockService.TakeResult = false;

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.PreviousProject);

        result.IsSuccess.Should().BeTrue();
        await _dialogService.DidNotReceiveWithAnyArgs().ShowAlertDialogAsync(default!, default!);
        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
        await _applicationShell.DidNotReceive().ExitApplicationAsync();
        _automationService.Received(1).LogEvent(new PreviousProjectSkippedEvent(ProjectFilePath));
        _automationService.DidNotReceive().LogEvent(Arg.Any<ProjectLoadFailedEvent>());
    }

    [TestCase(ProjectLoadOrigin.RunningWindow)]
    [TestCase(ProjectLoadOrigin.LaunchFile)]
    [TestCase(ProjectLoadOrigin.PreviousProject)]
    public async Task AProjectOpenInAnotherInstance_KeepsThePreviousProjectAndWritesNoLoadReport(ProjectLoadOrigin origin)
    {
        _projectLockService.TakeResult = false;
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(ProjectFilePath);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, origin);

        _settingsService.DidNotReceiveWithAnyArgs().Set(SettingCatalog.Project.PreviousProject, default!);
        _loadReporter.DidNotReceiveWithAnyArgs().BeginLoad(default!);
    }

    private void AssertTheCurrentProjectStaysOpen()
    {
        _projectService.DidNotReceive().ClearCurrentProject();
        _projectLockService.HeldProjectFiles.Should().Contain(CurrentProjectFilePath);
    }

    private void StubFolderProjectFiles(params string[] projectFilePaths)
    {
        IReadOnlyList<string> projectFiles = projectFilePaths.ToList();
        _projectService.GetProjectFilesAsync(Arg.Any<string>()).Returns(projectFiles.OkResult());
    }

    private void StubMigration(MigrationStatus status, string oldVersion, string newVersion)
    {
        var operationResult = status == MigrationStatus.UpgradeRequired
            ? Result.Ok()
            : Result.Fail("This version of Celbridge cannot open the project");
        var migrationResult = MigrationResult.WithVersions(status, operationResult, oldVersion, newVersion);
        _migrationService.CheckMigrationAsync(ProjectFilePath).Returns(migrationResult);
    }

    private void StubUpgradeAnswer(bool confirmed)
    {
        _dialogService.ShowConfirmationDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(Result<bool>.Ok(confirmed));
    }

    // Backed by the application's own resources, so the assertions read the text a user sees.
    private static IStringLocalizer CreateStringLocalizer()
    {
        var localizerService = new TestLocalizerService();
        var stringLocalizer = Substitute.For<IStringLocalizer>();

        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];

            return new LocalizedString(name, localizerService.GetString(name));
        });

        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            var arguments = (object[])callInfo[1];

            return new LocalizedString(name, localizerService.GetString(name, arguments));
        });

        return stringLocalizer;
    }
}
