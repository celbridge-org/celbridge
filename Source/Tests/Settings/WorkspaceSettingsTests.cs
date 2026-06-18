using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.Settings;

[TestFixture]
public class WorkspaceSettingsTests
{
    private string _rootFolderPath = null!;
    private LocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _rootFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WorkspaceSettingsTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootFolderPath);

        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootFolderPath))
        {
            Directory.Delete(_rootFolderPath, recursive: true);
        }
    }

    private WorkspaceSettingsService CreateService(string folderPath)
    {
        var service = new WorkspaceSettingsService(_fileSystem);
        service.WorkspaceSettingsFolderPath = folderPath;

        return service;
    }

    [Test]
    public async Task ICanAcquireAndUseWorkspaceSettingsAsync()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);

        var acquireResult = await service.AcquireWorkspaceSettingsAsync();
        acquireResult.IsSuccess.Should().BeTrue();

        var workspaceSettings = service.WorkspaceSettings;
        workspaceSettings.Should().NotBeNull();

        var dataVersion = await workspaceSettings!.GetDataVersionAsync();
        dataVersion.Should().Be(1);

        var expandedFolders = new List<string> { "a", "b", "c" };
        await workspaceSettings.SetPropertyAsync("ExpandedFolders", expandedFolders);

        var roundTripped = await workspaceSettings.GetPropertyAsync<List<string>>("ExpandedFolders");
        roundTripped.Should().NotBeNull();
        roundTripped!.Should().Equal(expandedFolders);

        // The settings file is written to the configured folder.
        var filePath = Path.Combine(folderPath, ProjectConstants.WorkspaceSettingsFile);
        File.Exists(filePath).Should().BeTrue();

        service.UnloadWorkspaceSettings();
        service.WorkspaceSettings.Should().BeNull();
    }

    [Test]
    public async Task WorkspaceSettings_SurviveUnloadAndReacquire()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();
        await service.WorkspaceSettings!.SetPropertyAsync("ExpandedFolders", new List<string> { "x" });

        service.UnloadWorkspaceSettings();

        await service.AcquireWorkspaceSettingsAsync();
        var reloaded = await service.WorkspaceSettings!.GetPropertyAsync<List<string>>("ExpandedFolders");

        reloaded.Should().NotBeNull();
        reloaded!.Should().Equal("x");
    }

    [Test]
    public async Task SecondProject_DoesNotSeeFirstProjectData()
    {
        var folderA = Path.Combine(_rootFolderPath, "projectA");
        var folderB = Path.Combine(_rootFolderPath, "projectB");

        var serviceA = CreateService(folderA);
        await serviceA.AcquireWorkspaceSettingsAsync();
        await serviceA.WorkspaceSettings!.SetPropertyAsync("ExpandedFolders", new List<string> { "from-A" });
        serviceA.UnloadWorkspaceSettings();

        var serviceB = CreateService(folderB);
        await serviceB.AcquireWorkspaceSettingsAsync();
        var valueFromB = await serviceB.WorkspaceSettings!.GetPropertyAsync<List<string>>("ExpandedFolders");

        valueFromB.Should().BeNull();
    }

    [Test]
    public async Task SyncStore_SharesDataWithAsyncFacade()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();

        // The async facade and the sync store are two views over the same data.
        await service.WorkspaceSettings!.SetPropertyAsync("Number", 99);

        var store = service.WorkspaceSettingsStore;
        store.Should().NotBeNull();
        store!.TryGetValue<int>("Number", out var value).Should().BeTrue();
        value.Should().Be(99);
    }

    [Test]
    public async Task SyncWrite_WithoutFlush_IsNotPersisted()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();
        service.WorkspaceSettingsStore!.SetValue("Count", 7);

        // No flush, so the in-memory change is dropped when the store is released.
        service.UnloadWorkspaceSettings();
        await service.AcquireWorkspaceSettingsAsync();

        service.WorkspaceSettingsStore!.TryGetValue<int>("Count", out _).Should().BeFalse();
    }

    [Test]
    public async Task SyncWrite_AfterFlush_IsPersisted()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();
        service.WorkspaceSettingsStore!.SetValue("Count", 7);
        await service.WorkspaceSettingsStore!.FlushAsync();

        service.UnloadWorkspaceSettings();
        await service.AcquireWorkspaceSettingsAsync();

        service.WorkspaceSettingsStore!.TryGetValue<int>("Count", out var value).Should().BeTrue();
        value.Should().Be(7);
    }
}
