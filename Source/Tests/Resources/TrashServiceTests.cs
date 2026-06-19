using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for TrashService — soft-delete moves, sidecar pairing, folder vs
/// file dispatch, restore round-trip, and purge cleanup.
/// </summary>
[TestFixture]
public class TrashServiceTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private TrashService _trashService = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(TrashServiceTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        // Default to failure for any unstubbed resolve; specific test setups
        // (per-Test) override with success results for the keys they exercise.
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Fail("not stubbed"));
        _resourceRegistry.GetResourceKey(Arg.Any<string>())
            .Returns(Result<ResourceKey>.Fail("not stubbed"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var sidecarService = new SidecarService(_workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

        _trashService = new TrashService(
            Substitute.For<ILogger<TrashService>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            ClearReadOnlyRecursive(_tempFolder);
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task MoveToTrashAsync_File_MovesFileIntoTrash()
    {
        var resource = new ResourceKey("file.txt");
        var path = Path.Combine(_tempFolder, "file.txt");
        await File.WriteAllTextAsync(path, "contents");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _trashService.MoveToTrashAsync(resource);

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
        var entry = result.Value;
        entry.WasFolder.Should().BeFalse();
        File.Exists(entry.TrashPath).Should().BeTrue();
        (await File.ReadAllTextAsync(entry.TrashPath)).Should().Be("contents");
    }

    [Test]
    public async Task MoveToTrashAsync_File_CascadesPairedSidecar()
    {
        var resource = new ResourceKey("doc.txt");
        var path = Path.Combine(_tempFolder, "doc.txt");
        var sidecarPath = path + SidecarFile.Extension;
        await File.WriteAllTextAsync(path, "main");
        await File.WriteAllTextAsync(sidecarPath, "+++\ntitle = 'Doc'\n+++\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("doc.txt.cel")).Returns(Result<string>.Ok(sidecarPath));

        var result = await _trashService.MoveToTrashAsync(resource);

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
        File.Exists(sidecarPath).Should().BeFalse();
        var entry = result.Value;
        entry.SidecarOriginalPath.Should().Be(sidecarPath);
        entry.SidecarTrashPath.Should().NotBeNull();
        File.Exists(entry.SidecarTrashPath!).Should().BeTrue();
    }

    [Test]
    public async Task MoveToTrashAsync_File_FailsWhenSourceMissing()
    {
        var resource = new ResourceKey("missing.txt");
        var path = Path.Combine(_tempFolder, "missing.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _trashService.MoveToTrashAsync(resource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task MoveToTrashAsync_EmptyFolder_RecordsEmptyFlag_AndRemovesFolder()
    {
        var resource = new ResourceKey("empty");
        var path = Path.Combine(_tempFolder, "empty");
        Directory.CreateDirectory(path);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _trashService.MoveToTrashAsync(resource);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(path).Should().BeFalse();
        var entry = result.Value;
        entry.WasFolder.Should().BeTrue();
        entry.WasEmptyFolder.Should().BeTrue();
        entry.TrashPath.Should().BeEmpty();
    }

    [Test]
    public async Task MoveToTrashAsync_NonEmptyFolder_MovesSubtree_AndCapturesDescendantKeys()
    {
        var resource = new ResourceKey("folder");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);
        var childPath = Path.Combine(folderPath, "child.txt");
        await File.WriteAllTextAsync(childPath, "child");

        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(folderPath));
        _resourceRegistry.GetResourceKey(childPath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/child.txt")));

        var result = await _trashService.MoveToTrashAsync(resource);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeFalse();
        var entry = result.Value;
        entry.WasFolder.Should().BeTrue();
        entry.WasEmptyFolder.Should().BeFalse();
        Directory.Exists(entry.TrashPath).Should().BeTrue();
        entry.DescendantKeys.Should().ContainSingle().Which.Path.Should().Be("folder/child.txt");
    }

    [Test]
    public async Task MoveToTrashAsync_NonEmptyFolder_CapturesNestedSubFolderKeys()
    {
        // Folders are first-class resources, so the descendant capture must cover
        // nested sub-folders too - including empty ones, which carry no files for
        // the file walk to surface.
        var resource = new ResourceKey("folder");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);

        var nestedFolderPath = Path.Combine(folderPath, "nested");
        Directory.CreateDirectory(nestedFolderPath);
        var deepFilePath = Path.Combine(nestedFolderPath, "deep.txt");
        await File.WriteAllTextAsync(deepFilePath, "deep");

        var emptyFolderPath = Path.Combine(folderPath, "empty");
        Directory.CreateDirectory(emptyFolderPath);

        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(folderPath));
        _resourceRegistry.GetResourceKey(deepFilePath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/nested/deep.txt")));
        _resourceRegistry.GetResourceKey(nestedFolderPath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/nested")));
        _resourceRegistry.GetResourceKey(emptyFolderPath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/empty")));

        var result = await _trashService.MoveToTrashAsync(resource);

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value;
        entry.DescendantKeys.Select(key => key.Path).Should().BeEquivalentTo(new[]
        {
            "folder/nested/deep.txt",
            "folder/nested",
            "folder/empty",
        });
    }

    [Test]
    public async Task RestoreFromTrashAsync_File_RestoresOriginalContent()
    {
        var resource = new ResourceKey("restore.txt");
        var path = Path.Combine(_tempFolder, "restore.txt");
        await File.WriteAllTextAsync(path, "before-trash");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var trashResult = await _trashService.MoveToTrashAsync(resource);
        trashResult.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeFalse();

        var restoreResult = await _trashService.RestoreFromTrashAsync(trashResult.Value);

        restoreResult.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("before-trash");
    }

    [Test]
    public async Task RestoreFromTrashAsync_EmptyFolder_RecreatesFolder()
    {
        var resource = new ResourceKey("empty");
        var path = Path.Combine(_tempFolder, "empty");
        Directory.CreateDirectory(path);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var trashResult = await _trashService.MoveToTrashAsync(resource);
        trashResult.IsSuccess.Should().BeTrue();

        var restoreResult = await _trashService.RestoreFromTrashAsync(trashResult.Value);

        restoreResult.IsSuccess.Should().BeTrue();
        Directory.Exists(path).Should().BeTrue();
    }

    [Test]
    public async Task RestoreFromTrashAsync_NonEmptyFolder_RestoresSubtree()
    {
        var resource = new ResourceKey("folder");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);
        var childPath = Path.Combine(folderPath, "child.txt");
        await File.WriteAllTextAsync(childPath, "kept");

        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(folderPath));
        _resourceRegistry.GetResourceKey(childPath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/child.txt")));

        var trashResult = await _trashService.MoveToTrashAsync(resource);
        trashResult.IsSuccess.Should().BeTrue();

        var restoreResult = await _trashService.RestoreFromTrashAsync(trashResult.Value);

        restoreResult.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
        File.Exists(childPath).Should().BeTrue();
        (await File.ReadAllTextAsync(childPath)).Should().Be("kept");
    }

    [Test]
    public async Task PurgeAsync_File_RemovesTrashBytes()
    {
        var resource = new ResourceKey("purgeme.txt");
        var path = Path.Combine(_tempFolder, "purgeme.txt");
        await File.WriteAllTextAsync(path, "doomed");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var trashResult = await _trashService.MoveToTrashAsync(resource);
        trashResult.IsSuccess.Should().BeTrue();
        var trashPath = trashResult.Value.TrashPath;
        File.Exists(trashPath).Should().BeTrue();

        var purgeResult = await _trashService.PurgeAsync(trashResult.Value);

        purgeResult.IsSuccess.Should().BeTrue();
        File.Exists(trashPath).Should().BeFalse();
    }

    [Test]
    public async Task MoveToTrashAsync_File_ClearsReadOnlyAttribute()
    {
        var resource = new ResourceKey("readonly.txt");
        var path = Path.Combine(_tempFolder, "readonly.txt");
        await File.WriteAllTextAsync(path, "locked");
        new FileInfo(path).IsReadOnly = true;
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        try
        {
            var result = await _trashService.MoveToTrashAsync(resource);

            result.IsSuccess.Should().BeTrue();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            // Ensure tear-down can delete the temp tree even if the test failed.
            ClearReadOnlyRecursive(_tempFolder);
        }
    }

    private static void ClearReadOnlyRecursive(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                if (info.Exists
                    && info.IsReadOnly)
                {
                    info.IsReadOnly = false;
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
