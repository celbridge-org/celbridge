using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for the ResourceMetaData cache file at .celbridge/cache/metadata.json.
/// Each test sets up a temporary project folder, runs a metadata instance through
/// some operations, disposes it (which flushes the cache), and then constructs a
/// second instance against the same folder to verify the persisted state hydrates
/// correctly.
/// </summary>
[TestFixture]
public class ResourceMetaDataPersistenceTests
{
    private string _projectFolderPath = null!;
    private IMessengerService _messengerService = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceMetaDataPersistenceTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _messengerService = new MessengerService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task SecondLoad_HydratesFromCache_WhenFilesUnchanged()
    {
        // First load: populate the index and dispose to flush the cache.
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "References \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target body.");

        await RunMetaDataAsync(metaData =>
        {
            var sourceKey = new ResourceKey("source.md");
            var targetKey = new ResourceKey("target.md");
            metaData.GetReferencers(targetKey).Should().Contain(sourceKey);
            return Task.CompletedTask;
        });

        // The cache file should have been created.
        var cachePath = Path.Combine(
            _projectFolderPath,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.CelbridgeCacheFolder,
            ProjectConstants.MetaDataCacheFileName);
        File.Exists(cachePath).Should().BeTrue("the cache file should be persisted on dispose");

        // Second load: the rebuild should pick up the cache. Since the files
        // are unchanged on disk, hydration validates mtime + size and skips
        // the scan; the resulting index entries match the first load.
        await RunMetaDataAsync(metaData =>
        {
            var sourceKey = new ResourceKey("source.md");
            var targetKey = new ResourceKey("target.md");
            metaData.GetReferencers(targetKey).Should().Contain(sourceKey);
            metaData.GetReferences(sourceKey).Should().Contain(targetKey);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SecondLoad_RescansFile_WhenContentChanged()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "References \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target body.");

        await RunMetaDataAsync(metaData =>
        {
            metaData.GetReferencers(new ResourceKey("target.md"))
                .Should().Contain(new ResourceKey("source.md"));
            return Task.CompletedTask;
        });

        // Mutate source.md to point at a different target. The mtime + size
        // both change, so the cache hydration must reject the cached entry and
        // fall through to a fresh scan.
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "Refers to \"project:other.md\" now.");
        // Make the mtime move forward so the comparison rejects the entry on
        // systems where the write is fast enough to keep the same tick.
        File.SetLastWriteTimeUtc(
            Path.Combine(_projectFolderPath, "source.md"),
            DateTime.UtcNow.AddSeconds(1));

        await RunMetaDataAsync(metaData =>
        {
            metaData.GetReferencers(new ResourceKey("target.md")).Should().BeEmpty();
            metaData.GetReferencers(new ResourceKey("other.md"))
                .Should().Contain(new ResourceKey("source.md"));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SecondLoad_DropsCachedEntry_WhenFileMissingOnDisk()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "References \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target body.");

        await RunMetaDataAsync(metaData =>
        {
            metaData.GetReferencers(new ResourceKey("target.md"))
                .Should().Contain(new ResourceKey("source.md"));
            return Task.CompletedTask;
        });

        // Delete source.md so the cache entry no longer corresponds to a real
        // file. Hydration should drop the entry and the index reflects the
        // missing source.
        File.Delete(Path.Combine(_projectFolderPath, "source.md"));

        await RunMetaDataAsync(metaData =>
        {
            metaData.GetReferencers(new ResourceKey("target.md")).Should().BeEmpty();
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SecondLoad_FullRebuild_WhenCacheJsonIsCorrupt()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "References \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target body.");

        await RunMetaDataAsync(_ => Task.CompletedTask);

        // Corrupt the cache file.
        var cachePath = Path.Combine(
            _projectFolderPath,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.CelbridgeCacheFolder,
            ProjectConstants.MetaDataCacheFileName);
        File.WriteAllText(cachePath, "this is not valid json {{{ ");

        // Should still load correctly via full rebuild.
        await RunMetaDataAsync(metaData =>
        {
            metaData.GetReferencers(new ResourceKey("target.md"))
                .Should().Contain(new ResourceKey("source.md"));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SecondLoad_FullRebuild_WhenCacheVersionMismatch()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "source.md"),
            "References \"project:target.md\".");
        File.WriteAllText(Path.Combine(_projectFolderPath, "target.md"), "Target body.");

        await RunMetaDataAsync(_ => Task.CompletedTask);

        // Write a cache file with a different version field. Should be
        // silently discarded and full rebuild runs.
        var cachePath = Path.Combine(
            _projectFolderPath,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.CelbridgeCacheFolder,
            ProjectConstants.MetaDataCacheFileName);
        File.WriteAllText(cachePath,
            "{\"version\":99,\"files\":{}}");

        await RunMetaDataAsync(metaData =>
        {
            metaData.GetReferencers(new ResourceKey("target.md"))
                .Should().Contain(new ResourceKey("source.md"));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SecondLoad_HydratesSidecarFrontmatter()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "Body.");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"),
            "+++\ntags = [\"flagged\", \"todo\"]\npriority = \"high\"\n+++\n");

        await RunMetaDataAsync(metaData =>
        {
            metaData.FindByTag("flagged").Should().Contain(new ResourceKey("notes.md"));
            return Task.CompletedTask;
        });

        await RunMetaDataAsync(metaData =>
        {
            // After reload via cache, the inverted index still returns the
            // resource for the same tag query.
            metaData.FindByTag("flagged").Should().Contain(new ResourceKey("notes.md"));
            metaData.FindByMetaData("priority", "high")
                .Should().Contain(new ResourceKey("notes.md"));
            return Task.CompletedTask;
        });
    }

    // Creates a fully-initialised ResourceMetaData against the test project
    // folder, awaits an initial rebuild, runs the test body, and disposes the
    // service (which flushes the cache). Each call simulates a workspace load /
    // unload cycle.
    private async Task RunMetaDataAsync(Func<ResourceMetaData, Task> body)
    {
        var fileIconService = new FileIconService();
        var registry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            _messengerService,
            fileIconService);
        registry.ProjectFolderPath = _projectFolderPath;

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(registry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var metaData = new ResourceMetaData(
            Substitute.For<ILogger<ResourceMetaData>>(),
            _messengerService,
            workspaceWrapper,
            new TextBinarySniffer());

        try
        {
            (await metaData.RebuildAsync()).IsSuccess.Should().BeTrue();
            await body(metaData);
        }
        finally
        {
            metaData.Dispose();
        }
    }
}
