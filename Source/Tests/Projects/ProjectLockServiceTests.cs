using Celbridge.Platform;
using Celbridge.Projects.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests the project lock against real files. Each service acts as one instance of the application. Two services
/// that share an application data folder contend for the same lock files. A test therefore checks that one service
/// has a lock by having the other service ask for the lock and be refused.
/// </summary>
[TestFixture]
public class ProjectLockServiceTests
{
    private const string LockFileName = "instance.lock";

    // Shorter than the lock service's retry delay.
    private const int MomentaryHoldMilliseconds = 50;

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
        await AssertAnotherInstanceIsRefusedAsync(_projectFilePath);

        var dataFolderPath = _dataFolderService.GetDataFolderPath(_projectFilePath);
        File.Exists(Path.Combine(dataFolderPath, LockFileName)).Should().BeTrue();
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
    }

    [Test]
    public async Task AReleasedLock_CanBeTakenByAnotherInstance()
    {
        await _otherInstanceLockService.TryTakeLockAsync(_projectFilePath);
        _otherInstanceLockService.ReleaseLock(_projectFilePath);

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

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
        await AssertAnotherInstanceIsRefusedAsync(_projectFilePath);
    }

    [Test]
    public async Task ARenamedProjectFile_IsCoveredByTheSameLock()
    {
        await _lockService.TryTakeLockAsync(_projectFilePath);

        var renamedProjectFilePath = Path.Combine(Path.GetDirectoryName(_projectFilePath)!, "Renamed.celbridge");
        File.Move(_projectFilePath, renamedProjectFilePath);

        await AssertAnotherInstanceIsRefusedAsync(renamedProjectFilePath);
    }

    [Test]
    public async Task ReleasingALockThisInstanceDoesNotHold_LeavesTheHoldersLock()
    {
        await _lockService.TryTakeLockAsync(_projectFilePath);

        _otherInstanceLockService.ReleaseLock(_projectFilePath);

        await AssertAnotherInstanceIsRefusedAsync(_projectFilePath);
    }

    [Test]
    public async Task ALockFileHeldOpenForAMoment_IsTakenOnTheRetry()
    {
        // A process such as a virus scanner can have the lock file open for a moment.
        var lockFilePath = await CreateLockFilePathAsync();
        var momentaryHold = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(MomentaryHoldMilliseconds);
            momentaryHold.Dispose();
        });

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);
        await releaseTask;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public async Task ALockFileThatCannotBeOpened_FailsRatherThanRefuses()
    {
        // Only another process with the lock file open refuses the lock. Any other failure to open the file is a
        // failure.
        var lockFilePath = await CreateLockFilePathAsync();
        Directory.CreateDirectory(lockFilePath);

        var result = await _lockService.TryTakeLockAsync(_projectFilePath);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task AMissingProjectFile_FailsToTakeTheLock()
    {
        var missingProjectFilePath = Path.Combine(_testFolderPath, "Missing", "Missing.celbridge");

        var result = await _lockService.TryTakeLockAsync(missingProjectFilePath);

        result.IsFailure.Should().BeTrue();
    }

    // The other instance is refused the lock only while this instance has it.
    private async Task AssertAnotherInstanceIsRefusedAsync(string projectFilePath)
    {
        var otherResult = await _otherInstanceLockService.TryTakeLockAsync(projectFilePath);

        otherResult.IsSuccess.Should().BeTrue(otherResult.MessageChain);
        otherResult.Value.Should().BeFalse();
    }

    private async Task<string> CreateLockFilePathAsync()
    {
        var dataFolderResult = await _dataFolderService.EnsureDataFolderAsync(_projectFilePath);
        dataFolderResult.IsSuccess.Should().BeTrue(dataFolderResult.MessageChain);

        return Path.Combine(dataFolderResult.Value, LockFileName);
    }
}
