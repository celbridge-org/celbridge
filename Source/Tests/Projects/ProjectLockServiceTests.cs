using Celbridge.Platform;
using Celbridge.Projects.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests the project lock against real files. Each service acts as one instance of the application. Two services
/// that share an application data folder contend for the same lock files.
/// </summary>
[TestFixture]
public class ProjectLockServiceTests
{
    private string _testFolderPath = null!;
    private string _projectFilePath = null!;
    private ProjectDataFolderService _dataFolderService = null!;
    private ProjectLockService _lockService = null!;
    private ProjectLockService _otherInstanceLockService = null!;

    [SetUp]
    public void Setup()
    {
        _testFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ProjectLockServiceTests),
            Guid.NewGuid().ToString("N"));

        var projectFolderPath = Path.Combine(_testFolderPath, "Project");
        Directory.CreateDirectory(projectFolderPath);
        _projectFilePath = Path.Combine(projectFolderPath, "Acme.celbridge");
        File.WriteAllText(_projectFilePath, string.Empty);

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.LocalApplicationDataFolderPath.Returns(Path.Combine(_testFolderPath, "AppData"));

        _dataFolderService = new ProjectDataFolderService(
            new NullLogger<ProjectDataFolderService>(),
            appEnvironment,
            TestFileSystem.CreateLocal());

        _lockService = new ProjectLockService(new NullLogger<ProjectLockService>(), _dataFolderService);
        _otherInstanceLockService = new ProjectLockService(new NullLogger<ProjectLockService>(), _dataFolderService);
    }

    [TearDown]
    public void TearDown()
    {
        _lockService.ReleaseLock(_projectFilePath);
        _otherInstanceLockService.ReleaseLock(_projectFilePath);

        if (Directory.Exists(_testFolderPath))
        {
            Directory.Delete(_testFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task TakingTheLock_CreatesTheLockFileInTheDataFolder()
    {
        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _lockService.HoldsLock(_projectFilePath).Should().BeTrue();

        var dataFolderPath = _dataFolderService.GetDataFolderPath(_projectFilePath);
        File.Exists(Path.Combine(dataFolderPath, "instance.lock")).Should().BeTrue();
    }

    [Test]
    public async Task ALockThisInstanceHolds_IsTakenAgain()
    {
        await _lockService.TryTakeLockAsync(_projectFilePath);

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

        result.Value.Should().BeTrue();
    }

    [Test]
    public async Task ALockAnotherInstanceHolds_IsRefused()
    {
        await _otherInstanceLockService.TryTakeLockAsync(_projectFilePath);

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        _lockService.HoldsLock(_projectFilePath).Should().BeFalse();
    }

    [Test]
    public async Task AReleasedLock_CanBeTakenByAnotherInstance()
    {
        await _otherInstanceLockService.TryTakeLockAsync(_projectFilePath);
        _otherInstanceLockService.ReleaseLock(_projectFilePath);

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

        _otherInstanceLockService.HoldsLock(_projectFilePath).Should().BeFalse();
        result.Value.Should().BeTrue();
    }

    [Test]
    public async Task AReleasedLock_CanBeTakenAgainByTheSameInstance()
    {
        // A reload releases the lock when the project closes, and takes the lock again when the project opens.
        await _lockService.TryTakeLockAsync(_projectFilePath);
        _lockService.ReleaseLock(_projectFilePath);

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

        result.Value.Should().BeTrue();
        _lockService.HoldsLock(_projectFilePath).Should().BeTrue();
    }

    [Test]
    public async Task ARenamedProjectFile_IsCoveredByTheSameLock()
    {
        await _lockService.TryTakeLockAsync(_projectFilePath);

        var renamedProjectFilePath = Path.Combine(Path.GetDirectoryName(_projectFilePath)!, "Renamed.celbridge");

        _lockService.HoldsLock(renamedProjectFilePath).Should().BeTrue();
    }

    [Test]
    public void ReleasingALockThisInstanceDoesNotHold_DoesNothing()
    {
        _lockService.ReleaseLock(_projectFilePath);

        _lockService.HoldsLock(_projectFilePath).Should().BeFalse();
    }

    [Test]
    public async Task AMissingProjectFile_FailsToTakeTheLock()
    {
        var missingProjectFilePath = Path.Combine(_testFolderPath, "Missing", "Missing.celbridge");

        var result = await _lockService.TryTakeLockAsync(missingProjectFilePath);

        result.IsFailure.Should().BeTrue();
    }
}
