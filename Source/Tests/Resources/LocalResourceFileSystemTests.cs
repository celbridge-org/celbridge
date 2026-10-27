using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for LocalResourceFileSystem — direct writes through the gateway,
/// parent-folder creation, ResolveResourcePath integration, reads,
/// stream-open happy paths, and the changes reported by each operation.
/// </summary>
[TestFixture]
public class LocalResourceFileSystemTests
{
    private string _tempFolder = null!;
    private string _tempRootFolder = null!;
    private IMessengerService _messengerService = null!;
    private IResourceMonitor _resourceMonitor = null!;
    private List<IReadOnlyList<ResourceKey>> _reports = null!;
    private LocalResourceFileSystem _resourceFileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(LocalResourceFileSystemTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
        _tempRootFolder = Path.Combine(_tempFolder, "Data", "temp");

        var rootHandlerRegistry = TestRootHandlers.Create(_tempFolder);
        rootHandlerRegistry.RegisterRootHandler(new TempRootHandler(_tempRootFolder));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var sidecarService = new SidecarService(workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

        // Records the keys of each report, in the order the reports arrive.
        _reports = new List<IReadOnlyList<ResourceKey>>();
        _resourceMonitor = Substitute.For<IResourceMonitor>();
        _resourceMonitor.ReportOperationChanges(
            Arg.Do<IReadOnlyList<ResourceKey>>(resources => _reports.Add(resources)));
        resourceService.Monitor.Returns(_resourceMonitor);

        _messengerService = Substitute.For<IMessengerService>();

        _resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            _messengerService,
            workspaceWrapper,
            TestFileSystem.CreateLocal());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task WriteAllBytesAsync_WritesContent_WhenFileDoesNotExist()
    {
        var resource = new ResourceKey("new.bin");
        var path = Path.Combine(_tempFolder, "new.bin");

        var bytes = new byte[] { 0x01, 0x02, 0x03 };

        var result = await _resourceFileSystem.WriteAllBytesAsync(resource, bytes);

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
    }

    [Test]
    public async Task WriteAllTextAsync_WritesContent_WhenFileDoesNotExist()
    {
        var resource = new ResourceKey("new.txt");
        var path = Path.Combine(_tempFolder, "new.txt");

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "hello world");

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("hello world");
    }

    [Test]
    public async Task WriteAllTextAsync_OverwritesExistingFile()
    {
        var resource = new ResourceKey("existing.txt");
        var path = Path.Combine(_tempFolder, "existing.txt");
        await File.WriteAllTextAsync(path, "old");

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "new");

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("new");
    }

    [Test]
    public async Task WriteAllTextAsync_CreatesIntermediateFolders()
    {
        var resource = new ResourceKey("nested/deeper/file.txt");
        var path = Path.Combine(_tempFolder, "nested", "deeper", "file.txt");

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "deep content");

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("deep content");
    }

    [Test]
    public async Task WriteAllTextAsync_ReportsTheFile()
    {
        var resource = new ResourceKey("notes.txt");

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "hello");

        result.IsSuccess.Should().BeTrue();
        _reports.Should().ContainSingle().Which.Should().Equal(resource);
    }

    [Test]
    public async Task WriteAllTextAsync_ReturnsFailure_WhenResolveFails()
    {
        // The root is unregistered, so resolving the key fails.
        var resource = new ResourceKey("unregistered:bad.txt");

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "anything");

        result.IsFailure.Should().BeTrue();
        _reports.Should().BeEmpty();
    }

    [Test]
    public async Task WriteAllBytesAsync_LeavesNoSiblingTempFile()
    {
        // Direct write: no staging or atomic-replace dance, so no temp files
        // should appear next to the destination.
        var resource = new ResourceKey("clean.bin");
        var path = Path.Combine(_tempFolder, "clean.bin");

        await _resourceFileSystem.WriteAllBytesAsync(resource, new byte[] { 0x42 });

        File.Exists(path + ".tmp").Should().BeFalse();
        Directory.EnumerateFiles(_tempFolder, "*.tmp").Should().BeEmpty();
    }

    [Test]
    public async Task ReadAllBytesAsync_ReturnsContent_WhenFileExists()
    {
        var resource = new ResourceKey("read.bin");
        var path = Path.Combine(_tempFolder, "read.bin");
        var expected = new byte[] { 0x10, 0x20, 0x30 };
        await File.WriteAllBytesAsync(path, expected);

        var result = await _resourceFileSystem.ReadAllBytesAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(expected);
    }

    [Test]
    public async Task ReadAllTextAsync_ReturnsContent_WhenFileExists()
    {
        var resource = new ResourceKey("read.txt");
        var path = Path.Combine(_tempFolder, "read.txt");
        await File.WriteAllTextAsync(path, "the content");

        var result = await _resourceFileSystem.ReadAllTextAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("the content");
    }

    [Test]
    public async Task ReadAllBytesAsync_ReturnsFailure_WhenFileMissing()
    {
        var resource = new ResourceKey("missing.bin");

        var result = await _resourceFileSystem.ReadAllBytesAsync(resource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task OpenReadAsync_ReturnsStreamWithFileContent()
    {
        var resource = new ResourceKey("openread.bin");
        var path = Path.Combine(_tempFolder, "openread.bin");
        var expected = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        await File.WriteAllBytesAsync(path, expected);

        var openResult = await _resourceFileSystem.OpenReadAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        await using var stream = openResult.Value;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(expected);
    }

    [Test]
    public async Task OpenWriteAsync_WritesContentThroughStream()
    {
        var resource = new ResourceKey("openwrite.bin");
        var path = Path.Combine(_tempFolder, "openwrite.bin");

        var openResult = await _resourceFileSystem.OpenWriteAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        var content = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await using (var stream = openResult.Value)
        {
            await stream.WriteAsync(content);
        }

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().Equal(content);
        _reports.Should().ContainSingle().Which.Should().Equal(resource);
    }

    [Test]
    public async Task OpenWriteAsync_CreatesParentFolder()
    {
        var resource = new ResourceKey("nested/folder/openwrite.bin");
        var path = Path.Combine(_tempFolder, "nested", "folder", "openwrite.bin");

        var openResult = await _resourceFileSystem.OpenWriteAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        await using (var stream = openResult.Value)
        {
            stream.WriteByte(0x99);
        }

        File.Exists(path).Should().BeTrue();
    }

    [Test]
    public async Task GetInfoAsync_ReturnsFile_WithSizeAndModifiedUtc_WhenFilePresent()
    {
        var resource = new ResourceKey("present.bin");
        var path = Path.Combine(_tempFolder, "present.bin");
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        await File.WriteAllBytesAsync(path, bytes);
        var expectedModifiedUtc = File.GetLastWriteTimeUtc(path);

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsSuccess.Should().BeTrue();
        var info = result.Value;
        info.Kind.Should().Be(StorageItemKind.File);
        info.Size.Should().Be(bytes.Length);
        info.ModifiedUtc.Should().Be(expectedModifiedUtc);
    }

    [Test]
    public async Task GetInfoAsync_ReturnsFolder_WhenFolderPresent()
    {
        var resource = new ResourceKey("nested");
        var folderPath = Path.Combine(_tempFolder, "nested");
        Directory.CreateDirectory(folderPath);

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsSuccess.Should().BeTrue();
        var info = result.Value;
        info.Kind.Should().Be(StorageItemKind.Folder);
        info.Size.Should().Be(0);
        info.ModifiedUtc.Should().NotBe(default);
    }

    [Test]
    public async Task GetInfoAsync_ReturnsNotFound_WhenResourceMissing()
    {
        var resource = new ResourceKey("missing.txt");

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsSuccess.Should().BeTrue();
        var info = result.Value;
        info.Kind.Should().Be(StorageItemKind.NotFound);
        info.Size.Should().Be(0);
        info.ModifiedUtc.Should().Be(default);
    }

    [Test]
    public async Task GetInfoAsync_ResolvesViaRegistry_ForNonDefaultRoot()
    {
        // A temp: key resolves through its own root handler, and is probed on disk the
        // same way as a project key.
        var resource = new ResourceKey("temp:scratch.txt");
        Directory.CreateDirectory(_tempRootFolder);
        var path = Path.Combine(_tempRootFolder, "scratch.txt");
        await File.WriteAllTextAsync(path, "scratch");

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.File);
        result.Value.Size.Should().Be("scratch".Length);
    }

    [Test]
    public async Task GetInfoAsync_ReturnsFailure_WhenResolveFails()
    {
        // The root is unregistered, so resolving the key fails.
        var resource = new ResourceKey("unregistered:bad.txt");

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetResourceItemAsync_ReturnsFileAndFolder()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_tempFolder, "nested"));

        var fileResult = await _resourceFileSystem.GetResourceItemAsync(new ResourceKey("notes.txt"));
        var folderResult = await _resourceFileSystem.GetResourceItemAsync(new ResourceKey("nested"));

        fileResult.Value.Should().Be(new ResourceItem(new ResourceKey("notes.txt"), IsFolder: false));
        folderResult.Value.Should().Be(new ResourceItem(new ResourceKey("nested"), IsFolder: true));
    }

    [Test]
    public async Task GetResourceItemAsync_Fails_WhenResourceMissing()
    {
        var result = await _resourceFileSystem.GetResourceItemAsync(new ResourceKey("missing.txt"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task MoveAsync_MovesFile_WhenNoSidecar()
    {
        var sourceKey = new ResourceKey("a.txt");
        var destKey = new ResourceKey("b.txt");
        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var destPath = Path.Combine(_tempFolder, "b.txt");
        await File.WriteAllTextAsync(sourcePath, "hello");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        (await File.ReadAllTextAsync(destPath)).Should().Be("hello");
        result.Value.Sidecar.Should().Be(SidecarOutcome.NotPresent);
    }

    [Test]
    public async Task MoveAsync_AcrossRoots_MovesTheFileWithoutAnnouncingAKeyChange()
    {
        var sourceKey = new ResourceKey("temp:downloads/staged.bin");
        var destKey = new ResourceKey("downloads/report.bin");
        var sourcePath = Path.Combine(_tempRootFolder, "downloads", "staged.bin");
        var destPath = Path.Combine(_tempFolder, "downloads", "report.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "payload");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey, new MoveOptions(AllowCrossRoot: true));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        File.Exists(sourcePath).Should().BeFalse();
        (await File.ReadAllTextAsync(destPath)).Should().Be("payload");

        // A move across roots counts as a delete and a create.
        _messengerService.DidNotReceive().Send(Arg.Any<ResourceKeyChangedMessage>());
    }

    [Test]
    public async Task MoveAsync_AcrossRoots_WithoutTheOption_IsRefused()
    {
        var sourceKey = new ResourceKey("temp:downloads/staged.bin");
        var destKey = new ResourceKey("downloads/report.bin");
        var sourcePath = Path.Combine(_tempRootFolder, "downloads", "staged.bin");
        var destPath = Path.Combine(_tempFolder, "downloads", "report.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "payload");

        // A move across roots carries none of the resource's identity, so a caller has to ask for one.
        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsFailure.Should().BeTrue();
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists(destPath).Should().BeFalse();
    }

    [Test]
    public async Task MoveAsync_OutOfTheProjectRoot_ReportsTheKeyInEachRoot()
    {
        var sourceKey = new ResourceKey("source.txt");
        var destKey = new ResourceKey("temp:source.txt");

        var sourcePath = Path.Combine(_tempFolder, "source.txt");
        var destPath = Path.Combine(_tempRootFolder, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "data");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey, new MoveOptions(AllowCrossRoot: true));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();

        _reports.Should().ContainSingle().Which.Should().Equal(sourceKey, destKey);
        _messengerService.DidNotReceive().Send(Arg.Any<ResourceKeyChangedMessage>());
    }

    [Test]
    public async Task MoveAsync_RenamesAFileByLetterCaseAlone()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.txt"), "hello");

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("notes.txt"), new ResourceKey("Notes.txt"));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        ListNames(_tempFolder).Should().Equal("Notes.txt");
        (await File.ReadAllTextAsync(Path.Combine(_tempFolder, "Notes.txt"))).Should().Be("hello");
    }

    [Test]
    public async Task MoveAsync_RenamesAFolderByLetterCaseAlone()
    {
        var folderPath = Path.Combine(_tempFolder, "docs");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "page.txt"), "page");

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("docs"), new ResourceKey("Docs"));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        ListNames(_tempFolder).Should().Equal("Docs");
        ListNames(Path.Combine(_tempFolder, "Docs")).Should().Equal("page.txt");
    }

    [Test]
    public async Task MoveAsync_RenamesTheSidecarWhenItsFileIsRenamedByLetterCaseAlone()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.md"), "notes");
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.md.cel"), "+++\n+++\n");

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("notes.md"), new ResourceKey("Notes.md"));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        ListNames(_tempFolder).Should().Equal("Notes.md", "Notes.md.cel");
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive volume, docs and Docs are different folders.")]
    public async Task MoveAsync_RefusesARenameIntoAFolderNamedInTheWrongCase()
    {
        var folderPath = Path.Combine(_tempFolder, "docs");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "notes.txt"), "notes");

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("docs/notes.txt"), new ResourceKey("Docs/Notes.txt"));

        result.IsFailure.Should().BeTrue();
        result.DiagnosticReport.Should().Contain("project:docs/notes.txt");
        ListNames(folderPath).Should().Equal("notes.txt");
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive volume, notes.txt and Notes.txt are different files.")]
    public async Task MoveAsync_RenameByLetterCaseAlone_ResolvesOnlyTheNewName()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.txt"), "hello");
        (await _resourceFileSystem.GetInfoAsync(new ResourceKey("notes.txt"))).IsSuccess.Should().BeTrue();

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("notes.txt"), new ResourceKey("Notes.txt"));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        var oldResult = await _resourceFileSystem.GetInfoAsync(new ResourceKey("notes.txt"));
        var newResult = await _resourceFileSystem.GetInfoAsync(new ResourceKey("Notes.txt"));
        oldResult.HasException<StoredNameMismatchError>().Should().BeTrue();
        newResult.IsSuccess.Should().BeTrue(newResult.DiagnosticReport);
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive volume, notes.txt and Notes.txt are different files.")]
    public async Task DeleteAsync_RecordsTheRemoval_SoTheOldNameIsRefused()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.txt"), "hello");
        (await _resourceFileSystem.GetInfoAsync(new ResourceKey("notes.txt"))).IsSuccess.Should().BeTrue();

        var result = await _resourceFileSystem.DeleteAsync(new ResourceKey("notes.txt"));

        // Another process makes the file again in another case, and the watcher has not reported it.
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "Notes.txt"), "again");
        var oldResult = await _resourceFileSystem.GetInfoAsync(new ResourceKey("notes.txt"));

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        oldResult.HasException<StoredNameMismatchError>().Should().BeTrue();
        oldResult.MessageChain.Should().Contain("Canonical form is 'project:Notes.txt'");
    }

    [Test]
    public async Task MoveAsync_CascadesSidecarWithFile()
    {
        var sourceKey = new ResourceKey("a.txt");
        var destKey = new ResourceKey("b.txt");

        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var destPath = Path.Combine(_tempFolder, "b.txt");
        var sourceSidecarPath = sourcePath + ".cel";
        var destSidecarPath = destPath + ".cel";
        await File.WriteAllTextAsync(sourcePath, "hello");
        await File.WriteAllTextAsync(sourceSidecarPath, "+++\n+++\n");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(sourceSidecarPath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        File.Exists(destSidecarPath).Should().BeTrue();

        // The file and its sidecar go out in one report.
        _reports.Should().ContainSingle().Which.Should().Equal(
            sourceKey,
            destKey,
            new ResourceKey("a.txt.cel"),
            new ResourceKey("b.txt.cel"));
    }

    [Test]
    public async Task MoveAsync_Folder_SendsOneKeyChangedMessage_AndReportsTheFolderAlone()
    {
        var sourceKey = new ResourceKey("folder");
        var destKey = new ResourceKey("renamed");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(Path.Combine(folderPath, "nested"));
        await File.WriteAllTextAsync(Path.Combine(folderPath, "nested", "deep.txt"), "deep");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        File.Exists(Path.Combine(_tempFolder, "renamed", "nested", "deep.txt")).Should().BeTrue();

        // Each receiver maps the keys inside the folder.
        _messengerService.Received(1).Send(Arg.Any<ResourceKeyChangedMessage>());
        _messengerService.Received(1).Send(new ResourceKeyChangedMessage(sourceKey, destKey));
        _reports.Should().ContainSingle().Which.Should().Equal(sourceKey, destKey);
    }

    [Test]
    public async Task MoveAsync_IntoAFolderBelowTheSource_IsRefused()
    {
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(Path.Combine(folderPath, "nested"));

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("folder"), new ResourceKey("folder/nested/folder"));

        result.IsFailure.Should().BeTrue();
        Directory.Exists(Path.Combine(folderPath, "nested", "folder")).Should().BeFalse();
        _messengerService.DidNotReceive().Send(Arg.Any<ResourceKeyChangedMessage>());
        _reports.Should().BeEmpty();
    }

    [Test]
    public async Task CopyAsync_IntoAFolderBelowTheSource_IsRefused()
    {
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(Path.Combine(folderPath, "nested"));

        var result = await _resourceFileSystem.CopyAsync(new ResourceKey("folder"), new ResourceKey("folder/nested/folder"));

        result.IsFailure.Should().BeTrue();
        Directory.Exists(Path.Combine(folderPath, "nested", "folder")).Should().BeFalse();
        _reports.Should().BeEmpty();
    }

    [Test]
    public async Task MoveAsync_ReportsAfterTheKeyChangedMessage()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "a.txt"), "hello");

        var result = await _resourceFileSystem.MoveAsync(new ResourceKey("a.txt"), new ResourceKey("b.txt"));

        // A tab that received the report first would find its old key absent, and close.
        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        Received.InOrder(() =>
        {
            _messengerService.Send(Arg.Any<ResourceKeyChangedMessage>());
            _resourceMonitor.ReportOperationChanges(Arg.Any<IReadOnlyList<ResourceKey>>());
        });
    }

    [Test]
    public async Task MoveAsync_WithinTheTempRoot_SendsAKeyChangedMessage()
    {
        var sourceKey = new ResourceKey("temp:a.txt");
        var destKey = new ResourceKey("temp:b.txt");
        Directory.CreateDirectory(_tempRootFolder);
        await File.WriteAllTextAsync(Path.Combine(_tempRootFolder, "a.txt"), "hello");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        _messengerService.Received(1).Send(new ResourceKeyChangedMessage(sourceKey, destKey));
    }

    [Test]
    public async Task MoveAsync_FailsWhenDestinationExists()
    {
        var sourceKey = new ResourceKey("a.txt");
        var destKey = new ResourceKey("b.txt");
        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var destPath = Path.Combine(_tempFolder, "b.txt");
        await File.WriteAllTextAsync(sourcePath, "src");
        await File.WriteAllTextAsync(destPath, "dst");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsFailure.Should().BeTrue();
        // Source still in place; destination unchanged.
        (await File.ReadAllTextAsync(sourcePath)).Should().Be("src");
        (await File.ReadAllTextAsync(destPath)).Should().Be("dst");
    }

    [Test]
    public async Task MoveAsync_LeavesAFileThatReferencesTheMovedResourceUnchanged()
    {
        var sourceKey = new ResourceKey("source.txt");
        var destKey = new ResourceKey("dest.txt");

        var sourcePath = Path.Combine(_tempFolder, "source.txt");
        var destPath = Path.Combine(_tempFolder, "dest.txt");
        var referencerPath = Path.Combine(_tempFolder, "doc.json");
        var referencerContent = "{\"target\": \"project:source.txt\"}";
        await File.WriteAllTextAsync(sourcePath, "data");
        await File.WriteAllTextAsync(referencerPath, referencerContent);

        var referencerWriteTime = File.GetLastWriteTimeUtc(referencerPath);

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        File.Exists(destPath).Should().BeTrue();
        (await File.ReadAllTextAsync(referencerPath)).Should().Be(referencerContent);
        File.GetLastWriteTimeUtc(referencerPath).Should().Be(referencerWriteTime);
    }

    [Test]
    public async Task CopyAsync_CopiesFile_AndCascadesSidecar()
    {
        var sourceKey = new ResourceKey("a.txt");
        var destKey = new ResourceKey("b.txt");
        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var destPath = Path.Combine(_tempFolder, "b.txt");
        var sourceSidecarPath = sourcePath + ".cel";
        var destSidecarPath = destPath + ".cel";

        await File.WriteAllTextAsync(sourcePath, "hello");
        await File.WriteAllTextAsync(sourceSidecarPath, "+++\n+++\n");

        var result = await _resourceFileSystem.CopyAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists(destPath).Should().BeTrue();
        File.Exists(sourceSidecarPath).Should().BeTrue();
        File.Exists(destSidecarPath).Should().BeTrue();
        _reports.Should().ContainSingle().Which.Should().Equal(destKey, new ResourceKey("b.txt.cel"));
    }

    [Test]
    public async Task DeleteAsync_DeletesFile_AndCascadesSidecar()
    {
        var sourceKey = new ResourceKey("a.txt");
        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var sourceSidecarPath = sourcePath + ".cel";
        await File.WriteAllTextAsync(sourcePath, "hello");
        await File.WriteAllTextAsync(sourceSidecarPath, "+++\n+++\n");

        var result = await _resourceFileSystem.DeleteAsync(sourceKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(sourceSidecarPath).Should().BeFalse();

        // The sidecar is deleted before its file.
        _reports.Should().ContainSingle().Which.Should().Equal(new ResourceKey("a.txt.cel"), sourceKey);
    }

    [Test]
    public async Task DeleteAsync_FailsWhenSourceMissing()
    {
        var sourceKey = new ResourceKey("missing.txt");

        var result = await _resourceFileSystem.DeleteAsync(sourceKey);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_Folder_ReportsTheFolderAlone()
    {
        var folderKey = new ResourceKey("folder");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(Path.Combine(folderPath, "nested"));
        await File.WriteAllTextAsync(Path.Combine(folderPath, "nested", "deep.txt"), "deep");

        var result = await _resourceFileSystem.DeleteAsync(folderKey);

        // A subscriber learns of the items inside the folder from the folder's change.
        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeFalse();
        _reports.Should().ContainSingle().Which.Should().Equal(folderKey);
    }

    [Test]
    public async Task ReadAllTextAsync_RetriesAndSucceeds_WhenLockReleasesQuickly()
    {
        var resource = new ResourceKey("locked.txt");
        var path = Path.Combine(_tempFolder, "locked.txt");
        await File.WriteAllTextAsync(path, "after release");

        // Briefly hold the file with FileShare.None so the first read attempt
        // hits a sharing violation, then release it before the retry budget
        // expires. The retry should succeed and return the file content.
        var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(75);
            lockStream.Dispose();
        });

        var result = await _resourceFileSystem.ReadAllTextAsync(resource);
        await releaseTask;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("after release");
    }

    [Test]
    public async Task DeleteAsync_DeletesReadOnlyFile()
    {
        var sourceKey = new ResourceKey("readonly.txt");
        var sourcePath = Path.Combine(_tempFolder, "readonly.txt");
        await File.WriteAllTextAsync(sourcePath, "content");
        new FileInfo(sourcePath).IsReadOnly = true;

        var result = await _resourceFileSystem.DeleteAsync(sourceKey);

        result.IsSuccess.Should().BeTrue();
        File.Exists(sourcePath).Should().BeFalse();
    }

    [Test]
    public async Task MoveAsync_MovesReadOnlyFile()
    {
        var sourceKey = new ResourceKey("readonly.txt");
        var destKey = new ResourceKey("renamed.txt");
        var sourcePath = Path.Combine(_tempFolder, "readonly.txt");
        var destPath = Path.Combine(_tempFolder, "renamed.txt");
        await File.WriteAllTextAsync(sourcePath, "content");
        new FileInfo(sourcePath).IsReadOnly = true;

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_CreatesFolder_WhenAbsent()
    {
        var folder = new ResourceKey("new-folder");
        var folderPath = Path.Combine(_tempFolder, "new-folder");

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
        _reports.Should().ContainSingle().Which.Should().Equal(folder);
    }

    [Test]
    public async Task CreateFolderAsync_IsIdempotent_WhenFolderAlreadyExists()
    {
        var folder = new ResourceKey("existing");
        var folderPath = Path.Combine(_tempFolder, "existing");
        Directory.CreateDirectory(folderPath);

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_CreatesMissingIntermediateParents()
    {
        var folder = new ResourceKey("outer/middle/inner");
        var folderPath = Path.Combine(_tempFolder, "outer", "middle", "inner");

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "outer", "middle")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "outer")).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_FailsWhenPathIsAlreadyAFile()
    {
        var folder = new ResourceKey("collision");
        var folderPath = Path.Combine(_tempFolder, "collision");
        await File.WriteAllTextAsync(folderPath, "I am a file");

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsFailure.Should().BeTrue();
        File.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_ReturnsFailure_WhenResolveFails()
    {
        // The root is unregistered, so resolving the key fails.
        var folder = new ResourceKey("unregistered:bad");

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task SetAttributesAsync_ReportsTheChange()
    {
        var resource = new ResourceKey("locked.txt");
        var path = Path.Combine(_tempFolder, "locked.txt");
        await File.WriteAllTextAsync(path, "content");

        try
        {
            var result = await _resourceFileSystem.SetAttributesAsync(resource, FileSystemAttributes.ReadOnly, set: true);

            result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
            new FileInfo(path).IsReadOnly.Should().BeTrue();
            _reports.Should().ContainSingle().Which.Should().Equal(resource);
        }
        finally
        {
            // Make the file writable again, so the tear-down can delete the temp folder.
            new FileInfo(path).IsReadOnly = false;
        }
    }

    [Test]
    public async Task WriteAllBytesAsync_PreservesReadOnlyAttribute_AndFails()
    {
        // A write leaves the read-only attribute in place. The underlying write throws UnauthorizedAccessException.
        // The gateway surfaces the exception as a failure.
        var resource = new ResourceKey("readonly.bin");
        var path = Path.Combine(_tempFolder, "readonly.bin");
        var originalBytes = new byte[] { 0x01, 0x02 };
        await File.WriteAllBytesAsync(path, originalBytes);
        new FileInfo(path).IsReadOnly = true;

        try
        {
            var result = await _resourceFileSystem.WriteAllBytesAsync(resource, new byte[] { 0xAA, 0xBB });

            result.IsFailure.Should().BeTrue();
            new FileInfo(path).IsReadOnly.Should().BeTrue();
            (await File.ReadAllBytesAsync(path)).Should().Equal(originalBytes);
        }
        finally
        {
            // Tear-down needs the file to be writable so the temp-folder delete works.
            if (File.Exists(path))
            {
                new FileInfo(path).IsReadOnly = false;
            }
        }
    }

    [Test]
    public async Task ReadAllBytesAsync_FailsImmediately_WhenFileMissing_WithoutRetry()
    {
        // FileNotFoundException is permanent; the retry budget should not be
        // spent on it. The test verifies fast failure by measuring elapsed time
        // — well under the total retry-budget upper bound (50+100+150 = 300ms).
        var resource = new ResourceKey("missing.bin");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _resourceFileSystem.ReadAllBytesAsync(resource);
        stopwatch.Stop();

        result.IsFailure.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    // Returns the stored names of a folder's items, in ordinal order.
    private static List<string> ListNames(string folderPath)
    {
        return Directory.EnumerateFileSystemEntries(folderPath)
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
