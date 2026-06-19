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

        var propertyBag = service.PropertyBag;
        propertyBag.Should().NotBeNull();

        var dataVersion = await propertyBag!.GetDataVersionAsync();
        dataVersion.Should().Be(1);

        var expandedFolders = new List<string> { "a", "b", "c" };
        await propertyBag.SetPropertyAsync("ExpandedFolders", expandedFolders);

        var roundTripped = await propertyBag.GetPropertyAsync<List<string>>("ExpandedFolders");
        roundTripped.Should().NotBeNull();
        roundTripped!.Should().Equal(expandedFolders);

        // The settings file is written to the configured folder.
        var filePath = Path.Combine(folderPath, ProjectConstants.WorkspaceSettingsFile);
        File.Exists(filePath).Should().BeTrue();

        service.UnloadWorkspaceSettings();
        service.PropertyBag.Should().BeNull();
    }

    [Test]
    public async Task PropertyBag_SurviveUnloadAndReacquire()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();
        await service.PropertyBag!.SetPropertyAsync("ExpandedFolders", new List<string> { "x" });

        service.UnloadWorkspaceSettings();

        await service.AcquireWorkspaceSettingsAsync();
        var reloaded = await service.PropertyBag!.GetPropertyAsync<List<string>>("ExpandedFolders");

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
        await serviceA.PropertyBag!.SetPropertyAsync("ExpandedFolders", new List<string> { "from-A" });
        serviceA.UnloadWorkspaceSettings();

        var serviceB = CreateService(folderB);
        await serviceB.AcquireWorkspaceSettingsAsync();
        var valueFromB = await serviceB.PropertyBag!.GetPropertyAsync<List<string>>("ExpandedFolders");

        valueFromB.Should().BeNull();
    }

    [Test]
    public async Task SyncStore_SharesDataWithAsyncFacade()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();

        // The async facade and the sync store are two views over the same data.
        await service.PropertyBag!.SetPropertyAsync("Number", 99);

        var store = service.WorkspaceSettingsStore;
        store.Should().NotBeNull();
        store!.TryGetValue<int>("Number", out var value).Should().BeTrue();
        value.Should().Be(99);
    }

    [Test]
    public async Task AcquireWorkspaceSettings_WhenAlreadyLoaded_DoesNotReloadOrDiscardInMemoryChanges()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var service = CreateService(folderPath);
        await service.AcquireWorkspaceSettingsAsync();

        // Write to the deferred store without flushing.
        service.WorkspaceSettingsStore!.SetValue("Count", 5);

        // The store is acquired twice during load (the page-load path before the
        // panels bind, then the workspace loader). The second acquire must be a
        // no-op: reloading from disk would discard this unflushed in-memory write.
        var reAcquireResult = await service.AcquireWorkspaceSettingsAsync();
        reAcquireResult.IsSuccess.Should().BeTrue();

        service.WorkspaceSettingsStore!.TryGetValue<int>("Count", out var value).Should().BeTrue();
        value.Should().Be(5);
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
