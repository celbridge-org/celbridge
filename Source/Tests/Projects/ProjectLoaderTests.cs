using Celbridge.Automation;
using Celbridge.Commands;
using Celbridge.Dependencies;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Messaging;
using Celbridge.Notifications;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Reports;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers ProjectLoader's workflow. Each test starts with another project open in this instance. Some checks can
/// refuse a project, and they run before that project closes. They cover a second project file in the folder, a
/// project already open here, the lock, the version and the upgrade prompt. A lock that another instance has taken
/// leads to a different outcome for each way a load can start. The tests also cover when the lock is kept and
/// released, and the load report. They also cover a load that fails or throws, and the alerts a refused version
/// raises. The trust dialog asks about an untrusted project after those checks and before the upgrade prompt. A
/// project that opens fully installs its features' dependencies before the workspace shows.
/// </summary>
[TestFixture]
public class ProjectLoaderTests
{
    private const string ProjectFilePath = "/projects/Acme/Acme.celbridge";
    private const string OtherProjectFilePath = "/projects/Acme/Other.celbridge";
    private const string CurrentProjectFilePath = "/projects/Current/Current.celbridge";
    private const string TrustDialogTitle = "Do you trust this project?";

    private IProjectMigrationService _migrationService = null!;
    private IProjectService _projectService = null!;
    private IProject? _currentProject;
    private Result<IProject> _projectLoadResult = null!;
    private FakeProjectLockService _projectLockService = null!;
    private IDialogService _dialogService = null!;
    private IApplicationShell _applicationShell = null!;
    private ISettingsService _settingsService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IAutomationService _automationService = null!;
    private IProjectLoadReporter _loadReporter = null!;
    private ICommandService _commandService = null!;
    private IDependencyService _dependencyService = null!;
    private ProjectUnloader _projectUnloader = null!;
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
        _commandService = Substitute.For<ICommandService>();
        _dependencyService = Substitute.For<IDependencyService>();

        // Another project is open in this instance, and this instance has the project's lock. The project service
        // tracks the current project as the loader and the unloader change it.
        _currentProject = CreateProject(CurrentProjectFilePath);
        _projectService.CurrentProject.Returns(_ => _currentProject);
        _projectService.When(service => service.ClearCurrentProject()).Do(_ => _currentProject = null);
        _projectLockService.HeldProjectFiles.Add(CurrentProjectFilePath);

        // By default the project's version is current, and its workspace loads. A test changes the project service's answer
        // through _projectLoadResult, since stubbing the call again would run this callback.
        _migrationService.CheckMigrationAsync(ProjectFilePath).Returns(MigrationResult.Success());
        StubFolderProjectFiles(ProjectFilePath);
        _projectLoadResult = Result<IProject>.Ok(CreateProject(ProjectFilePath));
        _projectService.LoadProjectAsync(Arg.Any<string>(), Arg.Any<MigrationResult>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                if (_projectLoadResult.IsSuccess)
                {
                    _currentProject = _projectLoadResult.Value;
                }

                return _projectLoadResult;
            });
        // By default the project is trusted, so no trust dialog shows.
        _projectService.IsTrustedAsync(Arg.Any<string>()).Returns(true);
        _projectService.TrustProjectAsync(Arg.Any<string>()).Returns(Result.Ok());
        _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>()).Returns(Result.Ok());
        _applicationShell.CloseWorkspaceAsync().Returns(Result.Ok());
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);

        _projectUnloader = new ProjectUnloader(
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
            _projectUnloader,
            _dialogService,
            _applicationShell,
            _settingsService,
            _workspaceWrapper,
            Substitute.For<IMessengerService>(),
            _commandService,
            _automationService,
            TestStringLocalizer.Create(),
            _loadReporter,
            _dependencyService);
    }

    [Test]
    public async Task AProjectThatOpensFully_InstallsItsDependenciesBeforeTheWorkspaceShows()
    {
        var features = new Dictionary<string, bool>
        {
            ["python"] = true,
        };
        _projectLoadResult = Result<IProject>.Ok(CreateFullyOpenedProject(ProjectFilePath, features));
        var progressToken = Substitute.For<IProgressDialogToken>();
        _dialogService.AcquireProgressDialog(Arg.Any<string>()).Returns(progressToken);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _dialogService.AcquireProgressDialog("Loading Acme");
            _dependencyService.InstallDependenciesAsync(features);
            _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>());
            progressToken.Dispose();
        });
        _projectService.DidNotReceive().RecordDependencyInstallFailure(Arg.Any<DependencyInstallFailure>());
    }

    [Test]
    public async Task AFailedInstall_RecordsTheFailureAndStillShowsTheWorkspace()
    {
        _projectLoadResult = Result<IProject>.Ok(CreateFullyOpenedProject(ProjectFilePath, new Dictionary<string, bool>()));
        var failure = new DependencyInstallFailure(FeatureFlagCatalog.Python, "uv", Result.Fail("The network is down"));
        _dependencyService.InstallDependenciesAsync(Arg.Any<IReadOnlyDictionary<string, bool>>()).Returns(failure);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _projectService.RecordDependencyInstallFailure(failure);
            _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>());
        });
    }

    [Test]
    public async Task AnUntrustedProject_InstallsNothing()
    {
        var project = CreateFullyOpenedProject(ProjectFilePath, new Dictionary<string, bool>());
        project.IsTrusted.Returns(false);
        _projectLoadResult = Result<IProject>.Ok(project);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _dependencyService.DidNotReceive().InstallDependenciesAsync(Arg.Any<IReadOnlyDictionary<string, bool>>());
    }

    [Test]
    public async Task AProjectFileThatFailsToLoad_InstallsNothing()
    {
        var project = CreateFullyOpenedProject(ProjectFilePath, new Dictionary<string, bool>());
        project.ConfigIsHealthy.Returns(false);
        _projectLoadResult = Result<IProject>.Ok(project);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _dependencyService.DidNotReceive().InstallDependenciesAsync(Arg.Any<IReadOnlyDictionary<string, bool>>());
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
    public async Task AProjectAlreadyOpenHere_IsNotLoadedAgain()
    {
        StubFolderProjectFiles(CurrentProjectFilePath);

        var result = await _projectLoader.LoadProjectAsync(CurrentProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _migrationService.DidNotReceiveWithAnyArgs().CheckMigrationAsync(default!);
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task AProjectOpenHereWithoutItsLock_IsNotLoadedAgain()
    {
        // When the lock fails for a reason other than another instance, the project opens without the lock.
        _projectLockService.HeldProjectFiles.Clear();
        StubFolderProjectFiles(CurrentProjectFilePath);

        var result = await _projectLoader.LoadProjectAsync(CurrentProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _migrationService.DidNotReceiveWithAnyArgs().CheckMigrationAsync(default!);
        _projectService.DidNotReceive().ClearCurrentProject();
        _projectLockService.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task AProjectFileRenamedOutsideCelbridge_IsTheProjectAlreadyOpenHere()
    {
        // The open project's file now has another name, and that file is the only project file in the folder.
        var renamedProjectFilePath = "/projects/Current/Renamed.celbridge";
        StubFolderProjectFiles(renamedProjectFilePath);

        var result = await _projectLoader.LoadProjectAsync(renamedProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task AnotherProjectFileInTheCurrentProjectsFolder_NamesTheCurrentProjectFile()
    {
        var copyProjectFilePath = "/projects/Current/Copy.celbridge";
        StubFolderProjectFiles(copyProjectFilePath, CurrentProjectFilePath);

        var result = await _projectLoader.LoadProjectAsync(copyProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Multiple Project Files",
            Arg.Is<string>(message => message.Contains("\"Current.celbridge\"")));
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.Calls.Should().BeEmpty();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(copyProjectFilePath));
    }

    [Test]
    public async Task ALoadThatFails_ClosesTheProjectAndReleasesTheLock()
    {
        _projectLoadResult = Result<IProject>.Fail("The load failed");

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        _currentProject.Should().BeNull();
        _projectLockService.HeldProjectFiles.Should().BeEmpty();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task AWorkspaceThatFailsToShow_ClosesTheProjectAndReleasesTheLock()
    {
        // The project service already has the project when the workspace fails to show.
        _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>())
            .Returns(Result.Fail("The workspace failed to show"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        _currentProject.Should().BeNull();
        _projectLockService.HeldProjectFiles.Should().BeEmpty();
        await _dialogService.Received(1).ShowAlertDialogAsync("Failed to Load Project", Arg.Any<string>());
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task AProjectOpenedAgainAfterAFailedLoad_KeepsItsLock()
    {
        _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>())
            .Returns(Result.Fail("The workspace failed to show"), Result.Ok());
        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        _currentProject!.ProjectFilePath.Should().Be(ProjectFilePath);
        _projectLockService.HeldProjectFiles.Should().Equal(ProjectFilePath);
    }

    [Test]
    public async Task ALoadThatThrowsBeforeTheCurrentProjectCloses_FailsAndLeavesTheCurrentProjectOpen()
    {
        _migrationService.CheckMigrationAsync(ProjectFilePath)
            .Returns<Task<MigrationResult>>(_ => throw new InvalidOperationException("The check threw"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.HeldProjectFiles.Should().NotContain(ProjectFilePath);
        _loadReporter.DidNotReceiveWithAnyArgs().BeginLoad(default!);
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task ALoadThatThrowsAfterTheCurrentProjectCloses_ClosesTheProjectAndReportsIt()
    {
        _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("The workspace threw"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        _currentProject.Should().BeNull();
        _projectLockService.HeldProjectFiles.Should().BeEmpty();
        _loadReporter.Received(1).RecordLoadOutcome(Arg.Is(false), Arg.Any<Result?>());
        await _loadReporter.Received(1).FlushAsync();
        await _dialogService.Received(1).ShowAlertDialogAsync("Failed to Load Project", Arg.Any<string>());
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task ACheckThatThrowsBeforeTheLock_ReportsTheFailure()
    {
        _projectService.FindProjectFilesAsync(Arg.Any<string>())
            .Returns<Task<Result<IReadOnlyList<string>>>>(_ => throw new InvalidOperationException("The folder check threw"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsFailure.Should().BeTrue();
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.Calls.Should().BeEmpty();
        _automationService.Received(1).LogEvent(new ProjectLoadFailedEvent(ProjectFilePath));
    }

    [Test]
    public async Task TheLoadReport_BeginsOnceTheCurrentProjectHasClosed()
    {
        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        Received.InOrder(() =>
        {
            _projectService.ClearCurrentProject();
            _loadReporter.BeginLoad(ProjectFilePath);
            _loadReporter.FlushAsync();
        });
    }

    [Test]
    public async Task ALockThatCannotBeTaken_LoadsTheProjectWithoutIt()
    {
        _projectLockService.TakeResult = Result<bool>.Fail("The data folder could not be created");

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>(), Arg.Any<bool>());
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

        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!, default);
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

        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!, default);
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
    public async Task ANewerVersion_WritesItsReportApartFromTheCurrentLoad()
    {
        StubMigration(MigrationStatus.NewerCelbridgeVersion, "1.4.0", "1.3.0");

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        await _loadReporter.Received(1).WriteRefusedLoadReportAsync(ProjectFilePath, Arg.Any<MigrationResult>(), Arg.Is(false));
        _loadReporter.DidNotReceiveWithAnyArgs().BeginLoad(default!);
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
        StubConfirmationAnswer(false);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        AssertTheCurrentProjectStaysOpen();
        _projectLockService.HeldProjectFiles.Should().NotContain(ProjectFilePath);
        await _migrationService.DidNotReceiveWithAnyArgs().PerformMigrationUpgradeAsync(default!);
        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!, default);
        _automationService.DidNotReceive().LogEvent(Arg.Any<ProjectLoadFailedEvent>());
    }

    [Test]
    public async Task ADeclinedUpgrade_WritesItsReportApartFromTheCurrentLoad()
    {
        StubMigration(MigrationStatus.UpgradeRequired, "1.2.0", "1.3.0");
        StubConfirmationAnswer(false);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        await _loadReporter.Received(1).WriteRefusedLoadReportAsync(ProjectFilePath, Arg.Any<MigrationResult>(), Arg.Is(true));
        _loadReporter.DidNotReceiveWithAnyArgs().BeginLoad(default!);
    }

    [Test]
    public async Task AConfirmedUpgrade_ClosesTheCurrentProjectBeforeUpgrading()
    {
        StubMigration(MigrationStatus.UpgradeRequired, "1.2.0", "1.3.0");
        StubConfirmationAnswer(true);
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
            "Multiple Project Files",
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
        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!, default);
    }

    [Test]
    public async Task AProjectOpenInAnotherInstance_AsThePreviousProject_ShowsHomeWithoutAnAlert()
    {
        _projectLockService.TakeResult = false;

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.PreviousProject);

        result.IsSuccess.Should().BeTrue();
        await _dialogService.DidNotReceiveWithAnyArgs().ShowAlertDialogAsync(default!, default!);
        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!, default);
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
        _currentProject!.ProjectFilePath.Should().Be(CurrentProjectFilePath);
        _projectLockService.HeldProjectFiles.Should().Contain(CurrentProjectFilePath);
    }

    [Test]
    public async Task AFailedUpgrade_ShowsItsSafeModeAlertOnceTheWorkspaceHasLoaded()
    {
        StubMigration(MigrationStatus.UpgradeRequired, "1.2.0", "1.3.0");

        // The user confirms the upgrade, and closes the Safe Mode alert.
        StubConfirmationAnswer(false);
        _dialogService.ShowConfirmationDialogAsync("Project Upgrade Required", Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(Result<bool>.Ok(true));
        _migrationService.PerformMigrationUpgradeAsync(ProjectFilePath)
            .Returns(MigrationResult.FromStatus(MigrationStatus.Failed, Result.Fail("The upgrade failed")));
        var safeModeService = TestSafeMode.Create(isActive: true);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>());
            _dialogService.ShowConfirmationDialogAsync(
                "Project Upgrade Failed",
                Arg.Is<string>(message => message.Contains("'Acme'") && message.Contains("Safe Mode")),
                Arg.Is<ConfirmationDialogOptions?>(options => IsSafeModeAlert(options)));
        });
        await _dialogService.DidNotReceive().ShowConfirmationDialogAsync(
            "Project Configuration Error",
            Arg.Any<string>(),
            Arg.Any<ConfirmationDialogOptions?>());
    }

    [TestCase(MigrationStatus.InvalidConfig)]
    [TestCase(MigrationStatus.Complete)]
    public async Task AConfigurationError_ShowsItsSafeModeAlertOnceTheWorkspaceHasLoaded(MigrationStatus migrationStatus)
    {
        // A configuration error fails the migration check. A project file that fails to parse passes the check
        // and fails as the project loads.
        StubMigration(migrationStatus, "1.3.0", "1.3.0");
        StubConfirmationAnswer(false);
        var safeModeService = TestSafeMode.Create(isActive: true);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _applicationShell.ShowWorkspaceAsync(Arg.Any<CancellationTokenSource>());
            _dialogService.ShowConfirmationDialogAsync(
                "Project Configuration Error",
                Arg.Is<string>(message => message.Contains("'Acme'") && message.Contains("Safe Mode")),
                Arg.Is<ConfirmationDialogOptions?>(options => IsSafeModeAlert(options)));
        });
    }

    [Test]
    public async Task ChoosingViewLoadReport_OpensTheReportWithTheLoadOutcome()
    {
        StubMigration(MigrationStatus.InvalidConfig, "1.3.0", "1.3.0");
        StubConfirmationAnswer(true);
        var safeModeService = TestSafeMode.Create(isActive: true);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        var reportResource = new ResourceKey("logs:reports/project-load.report");
        _loadReporter.FlushAsync().Returns(new ProjectLoadReportSummary(reportResource, ReportSeverity.Error, 1));
        Action<IOpenDocumentCommand>? configure = null;
        _commandService.Execute(
            Arg.Do<Action<IOpenDocumentCommand>>(action => configure = action),
            Arg.Any<string>(),
            Arg.Any<int>());

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        // The load records its outcome, then writes the report, then opens the report. The load writes the report
        // again as the load ends.
        Received.InOrder(() =>
        {
            _loadReporter.RecordLoadOutcome(true, Arg.Any<Result?>());
            _loadReporter.FlushAsync();
            _commandService.Execute(Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>());
            _loadReporter.FlushAsync();
        });

        configure.Should().NotBeNull();
        var openCommand = Substitute.For<IOpenDocumentCommand>();
        configure!(openCommand);
        openCommand.FileResource.Should().Be(reportResource);
    }

    [Test]
    public async Task ClosingTheSafeModeAlert_OpensNoReport()
    {
        StubMigration(MigrationStatus.InvalidConfig, "1.3.0", "1.3.0");
        StubConfirmationAnswer(false);
        var safeModeService = TestSafeMode.Create(isActive: true);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        _commandService.DidNotReceive().Execute(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task AProjectOutsideSafeMode_ShowsNoAlert()
    {
        var safeModeService = TestSafeMode.Create(isActive: false);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _dialogService.DidNotReceiveWithAnyArgs().ShowAlertDialogAsync(default!, default!);
        await _dialogService.DidNotReceiveWithAnyArgs().ShowConfirmationDialogAsync(default!, default!, default);
    }

    [Test]
    public async Task ATrustedProject_OpensFullyWithoutTheTrustDialog()
    {
        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await AssertTheTrustDialogNeverShows();
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>(), true);
    }

    [TestCase(ProjectLoadOrigin.RunningWindow)]
    [TestCase(ProjectLoadOrigin.PreviousProject)]
    [TestCase(ProjectLoadOrigin.LaunchFile)]
    public async Task AnUntrustedProject_AsksWhetherTheUserTrustsIt(ProjectLoadOrigin origin)
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubConfirmationAnswer(false);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, origin);

        // Open in Safe Mode is the accented default, so Return opens the project in Safe Mode.
        await _dialogService.Received(1).ShowConfirmationDialogAsync(
            TrustDialogTitle,
            Arg.Is<string>(message => message.Contains("Safe Mode")),
            Arg.Is<ConfirmationDialogOptions?>(options => IsTrustDialog(options)));
    }

    [Test]
    public async Task TheTrustDialog_NamesTheProjectFileBelowThePrompt()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubConfirmationAnswer(false);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        // ProjectFilePath is outside the home folder, so the message shows it in full, with no ~.
        await _dialogService.Received(1).ShowConfirmationDialogAsync(
            TrustDialogTitle,
            Arg.Is<string>(message => message.StartsWith("Projects can run code") && message.EndsWith($"\n\n{ProjectFilePath}")),
            Arg.Any<ConfirmationDialogOptions?>());
    }

    [Test]
    public async Task TheTrustDialog_ShowsWithTheLockHeldAndTheCurrentProjectStillOpen()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        var lockHeldWhenAsked = false;
        var currentProjectWhenAsked = string.Empty;
        _dialogService.ShowConfirmationDialogAsync(TrustDialogTitle, Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(_ =>
            {
                lockHeldWhenAsked = _projectLockService.HeldProjectFiles.Contains(ProjectFilePath);
                currentProjectWhenAsked = _currentProject?.ProjectFilePath ?? string.Empty;
                return Result<bool>.Ok(false);
            });

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        lockHeldWhenAsked.Should().BeTrue();
        currentProjectWhenAsked.Should().Be(CurrentProjectFilePath);
    }

    [Test]
    public async Task TrustProject_RecordsTheTrustAndOpensTheProjectFully()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubConfirmationAnswer(true);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _projectService.Received(1).TrustProjectAsync(ProjectFilePath);
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>(), true);
    }

    [Test]
    public async Task OpenInSafeMode_RecordsNothingAndOpensTheProjectUntrusted()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubConfirmationAnswer(false);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _projectService.DidNotReceive().TrustProjectAsync(Arg.Any<string>());
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>(), false);
    }

    [Test]
    public async Task AProjectOpenedInSafeMode_AsksAgainWhenItReloads()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubConfirmationAnswer(false);
        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        // A reload closes the project, then opens it again.
        await _projectUnloader.UnloadProjectAsync();
        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        await _dialogService.Received(2).ShowConfirmationDialogAsync(
            TrustDialogTitle,
            Arg.Any<string>(),
            Arg.Any<ConfirmationDialogOptions?>());
    }

    [Test]
    public async Task ATrustDialogThatFailsToShow_OpensTheProjectInSafeMode()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        _dialogService.ShowConfirmationDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(Result<bool>.Fail("Another dialog is open"));

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _projectService.DidNotReceive().TrustProjectAsync(Arg.Any<string>());
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>(), false);
    }

    [Test]
    public async Task ATrustThatCantBeRecorded_StillOpensTheProjectFully()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        _projectService.TrustProjectAsync(ProjectFilePath).Returns(Result.Fail("The data folder is read-only"));
        StubConfirmationAnswer(true);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        result.IsSuccess.Should().BeTrue();
        await _projectService.Received(1).LoadProjectAsync(ProjectFilePath, Arg.Any<MigrationResult>(), true);
    }

    [TestCase(MigrationStatus.NewerCelbridgeVersion, "1.4.0", "1.3.0")]
    [TestCase(MigrationStatus.UnsupportedCelbridgeVersion, "0.9.0", "1.3.0")]
    public async Task ARefusedVersion_NeverAsksAboutTrust(MigrationStatus status, string oldVersion, string newVersion)
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubMigration(status, oldVersion, newVersion);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        await AssertTheTrustDialogNeverShows();
    }

    [Test]
    public async Task ASecondProjectFile_NeverAsksAboutTrust()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubFolderProjectFiles(ProjectFilePath, OtherProjectFilePath);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        await AssertTheTrustDialogNeverShows();
    }

    [Test]
    public async Task AProjectAlreadyOpenHere_NeverAsksAboutTrust()
    {
        _projectService.IsTrustedAsync(CurrentProjectFilePath).Returns(false);
        StubFolderProjectFiles(CurrentProjectFilePath);

        await _projectLoader.LoadProjectAsync(CurrentProjectFilePath, ProjectLoadOrigin.RunningWindow);

        await AssertTheTrustDialogNeverShows();
    }

    [TestCase(ProjectLoadOrigin.RunningWindow)]
    [TestCase(ProjectLoadOrigin.LaunchFile)]
    [TestCase(ProjectLoadOrigin.PreviousProject)]
    public async Task AProjectOpenInAnotherInstance_NeverAsksAboutTrust(ProjectLoadOrigin origin)
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        _projectLockService.TakeResult = false;

        await _projectLoader.LoadProjectAsync(ProjectFilePath, origin);

        await AssertTheTrustDialogNeverShows();
    }

    [Test]
    public async Task TheTrustDialog_ComesBeforeTheUpgradePrompt()
    {
        _projectService.IsTrustedAsync(ProjectFilePath).Returns(false);
        StubMigration(MigrationStatus.UpgradeRequired, "1.2.0", "1.3.0");
        StubConfirmationAnswer(false);

        await _projectLoader.LoadProjectAsync(ProjectFilePath, ProjectLoadOrigin.RunningWindow);

        Received.InOrder(() =>
        {
            _dialogService.ShowConfirmationDialogAsync(
                TrustDialogTitle,
                Arg.Any<string>(),
                Arg.Any<ConfirmationDialogOptions?>());
            _dialogService.ShowConfirmationDialogAsync(
                "Project Upgrade Required",
                Arg.Any<string>(),
                Arg.Any<ConfirmationDialogOptions?>());
        });
    }

    private async Task AssertTheTrustDialogNeverShows()
    {
        await _dialogService.DidNotReceive().ShowConfirmationDialogAsync(
            TrustDialogTitle,
            Arg.Any<string>(),
            Arg.Any<ConfirmationDialogOptions?>());
    }

    private static bool IsTrustDialog(ConfirmationDialogOptions? options)
    {
        return options is not null
            && options.PrimaryButtonText == "Trust Project"
            && options.SecondaryButtonText == "Open in Safe Mode"
            && options.IsDestructive;
    }

    private static bool IsSafeModeAlert(ConfirmationDialogOptions? options)
    {
        return options is not null
            && options.PrimaryButtonText == "View Load Report"
            && options.SecondaryButtonText == "Close";
    }

    private static IProject CreateProject(string projectFilePath)
    {
        var project = Substitute.For<IProject>();
        project.ProjectName.Returns(Path.GetFileNameWithoutExtension(projectFilePath));
        project.ProjectFilePath.Returns(projectFilePath);

        return project;
    }

    // A trusted project with a project file that loaded cleanly. The project opens with none of the causes of Safe
    // Mode.
    private static IProject CreateFullyOpenedProject(string projectFilePath, IReadOnlyDictionary<string, bool> features)
    {
        var project = CreateProject(projectFilePath);
        project.IsTrusted.Returns(true);
        project.ConfigIsHealthy.Returns(true);
        project.Config.Returns(new ProjectConfig
        {
            Features = features
        });

        return project;
    }

    private void StubFolderProjectFiles(params string[] projectFilePaths)
    {
        IReadOnlyList<string> projectFiles = projectFilePaths.ToList();
        _projectService.FindProjectFilesAsync(Arg.Any<string>()).Returns(projectFiles.OkResult());
    }

    private void StubMigration(MigrationStatus status, string oldVersion, string newVersion)
    {
        var operationResult = status == MigrationStatus.UpgradeRequired
            ? Result.Ok()
            : Result.Fail("This version of Celbridge cannot open the project");
        var migrationResult = MigrationResult.WithVersions(status, operationResult, oldVersion, newVersion);
        _migrationService.CheckMigrationAsync(ProjectFilePath).Returns(migrationResult);
    }

    private void StubConfirmationAnswer(bool confirmed)
    {
        _dialogService.ShowConfirmationDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(Result<bool>.Ok(confirmed));
    }

    // Backed by the application's own resources, so the assertions read the text a user sees.
}
