using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for LocalResourceFileSystem — direct writes through the gateway,
/// parent-folder creation, ResolveResourcePath integration, reads, and
/// stream-open happy paths.
/// </summary>
[TestFixture]
public class LocalResourceFileSystemTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IRootHandlerRegistry _rootHandlerRegistry = null!;
    private IResourceScanner _resourceScanner = null!;
    private IResourceOperationService _operationService = null!;
    private IMessengerService _messengerService = null!;
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

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        // Default to failure for any unstubbed resolve; specific tests override
        // with success results for the keys they exercise.
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Fail("not stubbed"));

        _resourceScanner = Substitute.For<IResourceScanner>();
        _resourceScanner.FindReferencersAsync(Arg.Any<ResourceKey>()).Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(Array.Empty<ResourceKey>()));
        _resourceScanner.FindAllReferencedTargetsAsync().Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(Array.Empty<ResourceKey>()));

        _rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        _rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>());
        // Default to failure for any unstubbed resolve; folder operations stub
        // the descendant paths they exercise.
        _rootHandlerRegistry.GetResourceKey(Arg.Any<string>())
            .Returns(Result<ResourceKey>.Fail("not stubbed"));

        // Default the writable cache to Writable so existing tests behave as
        // they did before the read-only-strip gate was added. The two tests
        // that exercise the gate override this stub explicitly.
        _operationService = Substitute.For<IResourceOperationService>();
        _operationService.GetWritableStateAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(WritableState.Writable));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlers.Returns(_rootHandlerRegistry);
        resourceService.Operations.Returns(_operationService);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Scanner.Returns(_resourceScanner);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var sidecarService = new SidecarService(workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "new");

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("new");
    }

    [Test]
    public async Task WriteAllTextAsync_CreatesIntermediateFolders()
    {
        var resource = new ResourceKey("nested/deeper/file.txt");
        var path = Path.Combine(_tempFolder, "nested", "deeper", "file.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "deep content");

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("deep content");
    }

    [Test]
    public async Task WriteAllTextAsync_ReturnsFailure_WhenResolveFails()
    {
        var resource = new ResourceKey("bad.txt");
        _resourceRegistry.ResolveResourcePath(resource)
            .Returns(Result<string>.Fail("simulated resolve failure"));

        var result = await _resourceFileSystem.WriteAllTextAsync(resource, "anything");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task WriteAllBytesAsync_LeavesNoSiblingTempFile()
    {
        // Direct write: no staging or atomic-replace dance, so no temp files
        // should appear next to the destination.
        var resource = new ResourceKey("clean.bin");
        var path = Path.Combine(_tempFolder, "clean.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _resourceFileSystem.ReadAllTextAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("the content");
    }

    [Test]
    public async Task ReadAllBytesAsync_ReturnsFailure_WhenFileMissing()
    {
        var resource = new ResourceKey("missing.bin");
        var path = Path.Combine(_tempFolder, "missing.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var openResult = await _resourceFileSystem.OpenWriteAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        var content = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await using (var stream = openResult.Value)
        {
            await stream.WriteAsync(content);
        }

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().Equal(content);
    }

    [Test]
    public async Task OpenWriteAsync_CreatesParentFolder()
    {
        var resource = new ResourceKey("nested/folder/openwrite.bin");
        var path = Path.Combine(_tempFolder, "nested", "folder", "openwrite.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(folderPath));

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
        var path = Path.Combine(_tempFolder, "missing.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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
        // Non-default-root callers route through IResourceRegistry the same way
        // default-root callers do: the gateway hands the key off, the
        // registry resolves it to an absolute path, and the on-disk probe is
        // identical. This test pins the contract end-to-end against a temp:
        // key so a future regression in the resolution wiring surfaces here.
        var resource = new ResourceKey("temp:scratch.txt");
        var stagingFolder = Path.Combine(_tempFolder, ".celbridge", "scratch");
        Directory.CreateDirectory(stagingFolder);
        var path = Path.Combine(stagingFolder, "scratch.txt");
        await File.WriteAllTextAsync(path, "scratch");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.File);
        result.Value.Size.Should().Be("scratch".Length);
    }

    [Test]
    public async Task GetInfoAsync_ReturnsFailure_WhenResolveFails()
    {
        var resource = new ResourceKey("bad.txt");
        _resourceRegistry.ResolveResourcePath(resource)
            .Returns(Result<string>.Fail("simulated resolve failure"));

        var result = await _resourceFileSystem.GetInfoAsync(resource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task MoveAsync_MovesFile_WhenNoReferencersAndNoSidecar()
    {
        var sourceKey = new ResourceKey("a.txt");
        var destKey = new ResourceKey("b.txt");
        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var destPath = Path.Combine(_tempFolder, "b.txt");
        await File.WriteAllTextAsync(sourcePath, "hello");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        var sidecarSource = new ResourceKey("a.txt.cel");
        var sidecarDest = new ResourceKey("b.txt.cel");
        _resourceRegistry.ResolveResourcePath(sidecarSource).Returns(Result<string>.Ok(sourcePath + ".cel"));
        _resourceRegistry.ResolveResourcePath(sidecarDest).Returns(Result<string>.Ok(destPath + ".cel"));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        (await File.ReadAllTextAsync(destPath)).Should().Be("hello");
        result.Value.UpdatedReferencers.Should().BeEmpty();
        result.Value.Sidecar.Should().Be(SidecarOutcome.NotPresent);
    }

    [Test]
    public async Task MoveAsync_FailsCrossRootPrecondition()
    {
        var sourceKey = new ResourceKey("project:a.txt");
        var destKey = new ResourceKey("temp:a.txt");

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("same root");
    }

    [Test]
    public async Task MoveAsync_CascadesSidecarWithFile()
    {
        var sourceKey = new ResourceKey("a.txt");
        var destKey = new ResourceKey("b.txt");
        var sidecarSource = new ResourceKey("a.txt.cel");
        var sidecarDest = new ResourceKey("b.txt.cel");

        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var destPath = Path.Combine(_tempFolder, "b.txt");
        var sourceSidecarPath = sourcePath + ".cel";
        var destSidecarPath = destPath + ".cel";
        await File.WriteAllTextAsync(sourcePath, "hello");
        await File.WriteAllTextAsync(sourceSidecarPath, "+++\n+++\n");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(sidecarSource).Returns(Result<string>.Ok(sourceSidecarPath));
        _resourceRegistry.ResolveResourcePath(sidecarDest).Returns(Result<string>.Ok(destSidecarPath));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(sourceSidecarPath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        File.Exists(destSidecarPath).Should().BeTrue();
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

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsFailure.Should().BeTrue();
        // Source still in place; destination unchanged.
        (await File.ReadAllTextAsync(sourcePath)).Should().Be("src");
        (await File.ReadAllTextAsync(destPath)).Should().Be("dst");
    }

    [Test]
    public async Task MoveAsync_RewritesReferencers()
    {
        var sourceKey = new ResourceKey("source.txt");
        var destKey = new ResourceKey("dest.txt");
        var referencerKey = new ResourceKey("doc.json");

        var sourcePath = Path.Combine(_tempFolder, "source.txt");
        var destPath = Path.Combine(_tempFolder, "dest.txt");
        var referencerPath = Path.Combine(_tempFolder, "doc.json");
        await File.WriteAllTextAsync(sourcePath, "data");
        await File.WriteAllTextAsync(referencerPath, "See \"project:source.txt\" for details.");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(referencerKey).Returns(Result<string>.Ok(referencerPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("source.txt.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("dest.txt.cel")).Returns(Result<string>.Ok(destPath + ".cel"));

        _resourceScanner.FindReferencersAsync(sourceKey).Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(new[] { referencerKey }));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.UpdatedReferencers.Should().Contain(referencerKey);
        (await File.ReadAllTextAsync(referencerPath)).Should().Be("See \"project:dest.txt\" for details.");
    }

    [Test]
    public async Task MoveAsync_DoesNotRewriteUnquotedOccurrencesAtFileBoundaries()
    {
        // The rewrite cascade visits files the scanner indexed. Within a
        // visited file, IndexOf may also match incidental occurrences of the
        // source literal that aren't quoted references. Boundary checks must
        // reject those — including at position 0 and end-of-text, where an
        // earlier implementation short-circuited the check and silently
        // rewrote unquoted byte sequences.
        var sourceKey = new ResourceKey("source.txt");
        var destKey = new ResourceKey("dest.txt");
        var referencerKey = new ResourceKey("doc.json");

        var sourcePath = Path.Combine(_tempFolder, "source.txt");
        var destPath = Path.Combine(_tempFolder, "dest.txt");
        var referencerPath = Path.Combine(_tempFolder, "doc.json");
        await File.WriteAllTextAsync(sourcePath, "data");

        // The file contains the literal "project:source.txt" at three positions:
        // 1. Start of file, no leading quote (incidental, must NOT rewrite).
        // 2. Middle, properly quoted (real reference, MUST rewrite).
        // 3. End of file, no trailing quote (incidental, must NOT rewrite).
        var initialContent = "project:source.txt at start. See \"project:source.txt\" here. Trailing project:source.txt";
        await File.WriteAllTextAsync(referencerPath, initialContent);

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(referencerKey).Returns(Result<string>.Ok(referencerPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("source.txt.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("dest.txt.cel")).Returns(Result<string>.Ok(destPath + ".cel"));

        _resourceScanner.FindReferencersAsync(sourceKey).Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(new[] { referencerKey }));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.UpdatedReferencers.Should().Contain(referencerKey);

        // Only the middle (quoted) occurrence is rewritten. The unquoted ones
        // at the start and end of the file remain pointing at the old name.
        var expected = "project:source.txt at start. See \"project:dest.txt\" here. Trailing project:source.txt";
        (await File.ReadAllTextAsync(referencerPath)).Should().Be(expected);
    }

    [Test]
    public async Task MoveAsync_RewritesQuotedReferencerWithSpaceInKey()
    {
        // A reference inside ASCII double quotes — the only delimiter that
        // allows whitespace in the key under Option C — must be rewritten by
        // the cascade with the same delimiter-aware boundary check used by
        // detection.
        var sourceKey = new ResourceKey("My Document.md");
        var destKey = new ResourceKey("My Renamed Document.md");
        var referencerKey = new ResourceKey("doc.json");

        var sourcePath = Path.Combine(_tempFolder, "My Document.md");
        var destPath = Path.Combine(_tempFolder, "My Renamed Document.md");
        var referencerPath = Path.Combine(_tempFolder, "doc.json");
        await File.WriteAllTextAsync(sourcePath, "data");
        await File.WriteAllTextAsync(referencerPath,
            "See \"project:My Document.md\" and also 'project:My Document.md' as well.");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(referencerKey).Returns(Result<string>.Ok(referencerPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("My Document.md.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("My Renamed Document.md.cel")).Returns(Result<string>.Ok(destPath + ".cel"));

        _resourceScanner.FindReferencersAsync(sourceKey).Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(new[] { referencerKey }));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.UpdatedReferencers.Should().Contain(referencerKey);
        (await File.ReadAllTextAsync(referencerPath)).Should().Be(
            "See \"project:My Renamed Document.md\" and also 'project:My Renamed Document.md' as well.");
    }

    [Test]
    public async Task MoveAsync_RewritesJsonEscapedReferencer()
    {
        // The reference sits inside a JSON-escape sequence \"project:...\"
        // (e.g. an MCP tool response stored as a JSON string). The scanner
        // detects it via the two-char \" opener and the cascade rewrites it
        // through the same parser path so the trailing \" is recognised.
        var sourceKey = new ResourceKey("foo.md");
        var destKey = new ResourceKey("bar.md");
        var referencerKey = new ResourceKey("payload.json");

        var sourcePath = Path.Combine(_tempFolder, "foo.md");
        var destPath = Path.Combine(_tempFolder, "bar.md");
        var referencerPath = Path.Combine(_tempFolder, "payload.json");
        await File.WriteAllTextAsync(sourcePath, "data");
        await File.WriteAllTextAsync(referencerPath,
            "{\"description\": \"See \\\"project:foo.md\\\" for details\"}");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(referencerKey).Returns(Result<string>.Ok(referencerPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("foo.md.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("bar.md.cel")).Returns(Result<string>.Ok(destPath + ".cel"));

        _resourceScanner.FindReferencersAsync(sourceKey).Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(new[] { referencerKey }));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.UpdatedReferencers.Should().Contain(referencerKey);
        (await File.ReadAllTextAsync(referencerPath)).Should().Be(
            "{\"description\": \"See \\\"project:bar.md\\\" for details\"}");
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

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("a.txt.cel")).Returns(Result<string>.Ok(sourceSidecarPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("b.txt.cel")).Returns(Result<string>.Ok(destSidecarPath));

        var result = await _resourceFileSystem.CopyAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists(destPath).Should().BeTrue();
        File.Exists(sourceSidecarPath).Should().BeTrue();
        File.Exists(destSidecarPath).Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_DeletesFile_AndCascadesSidecar()
    {
        var sourceKey = new ResourceKey("a.txt");
        var sourcePath = Path.Combine(_tempFolder, "a.txt");
        var sourceSidecarPath = sourcePath + ".cel";
        await File.WriteAllTextAsync(sourcePath, "hello");
        await File.WriteAllTextAsync(sourceSidecarPath, "+++\n+++\n");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("a.txt.cel")).Returns(Result<string>.Ok(sourceSidecarPath));

        var result = await _resourceFileSystem.DeleteAsync(sourceKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sidecar.Should().Be(SidecarOutcome.Cascaded);
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(sourceSidecarPath).Should().BeFalse();
    }

    [Test]
    public async Task DeleteAsync_FailsWhenSourceMissing()
    {
        var sourceKey = new ResourceKey("missing.txt");
        var sourcePath = Path.Combine(_tempFolder, "missing.txt");

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));

        var result = await _resourceFileSystem.DeleteAsync(sourceKey);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_Folder_FansOutDeleteMessages_ForNestedSubFolders()
    {
        // Folders are first-class resources, so deleting a folder must announce
        // the removal of nested sub-folders too - including empty ones the file
        // walk cannot surface - not just the files beneath them.
        var folderKey = new ResourceKey("folder");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);

        var nestedFolderPath = Path.Combine(folderPath, "nested");
        Directory.CreateDirectory(nestedFolderPath);
        var deepFilePath = Path.Combine(nestedFolderPath, "deep.txt");
        await File.WriteAllTextAsync(deepFilePath, "deep");

        var emptyFolderPath = Path.Combine(folderPath, "empty");
        Directory.CreateDirectory(emptyFolderPath);

        _resourceRegistry.ResolveResourcePath(folderKey).Returns(Result<string>.Ok(folderPath));
        _rootHandlerRegistry.GetResourceKey(deepFilePath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/nested/deep.txt")));
        _rootHandlerRegistry.GetResourceKey(nestedFolderPath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/nested")));
        _rootHandlerRegistry.GetResourceKey(emptyFolderPath).Returns(Result<ResourceKey>.Ok(new ResourceKey("folder/empty")));

        var deletedKeys = new List<ResourceKey>();
        _messengerService.Send(Arg.Do<ResourceDeletedMessage>(message => deletedKeys.Add(message.Resource)));

        var result = await _resourceFileSystem.DeleteAsync(folderKey);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeFalse();
        deletedKeys.Select(key => key.Path).Should().BeEquivalentTo(new[]
        {
            "folder",
            "folder/nested/deep.txt",
            "folder/nested",
            "folder/empty",
        });
    }

    [Test]
    public async Task ReadAllTextAsync_RetriesAndSucceeds_WhenLockReleasesQuickly()
    {
        var resource = new ResourceKey("locked.txt");
        var path = Path.Combine(_tempFolder, "locked.txt");
        await File.WriteAllTextAsync(path, "after release");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

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

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("readonly.txt.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));

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

        _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
        _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("readonly.txt.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));
        _resourceRegistry.ResolveResourcePath(new ResourceKey("renamed.txt.cel")).Returns(Result<string>.Ok(destPath + ".cel"));

        var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

        result.IsSuccess.Should().BeTrue();
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
    }

    [Test]
    public async Task MoveAsync_SkipsReadOnlyReferencer_AndReportsItInResult()
    {
        var sourceKey = new ResourceKey("target.txt");
        var destKey = new ResourceKey("target2.txt");
        var referencerKey = new ResourceKey("doc.json");

        var sourcePath = Path.Combine(_tempFolder, "target.txt");
        var destPath = Path.Combine(_tempFolder, "target2.txt");
        var referencerPath = Path.Combine(_tempFolder, "doc.json");
        await File.WriteAllTextAsync(sourcePath, "data");
        await File.WriteAllTextAsync(referencerPath, "See \"project:target.txt\" for details.");
        new FileInfo(referencerPath).IsReadOnly = true;

        try
        {
            _resourceRegistry.ResolveResourcePath(sourceKey).Returns(Result<string>.Ok(sourcePath));
            _resourceRegistry.ResolveResourcePath(destKey).Returns(Result<string>.Ok(destPath));
            _resourceRegistry.ResolveResourcePath(referencerKey).Returns(Result<string>.Ok(referencerPath));
            _resourceRegistry.ResolveResourcePath(new ResourceKey("target.txt.cel")).Returns(Result<string>.Ok(sourcePath + ".cel"));
            _resourceRegistry.ResolveResourcePath(new ResourceKey("target2.txt.cel")).Returns(Result<string>.Ok(destPath + ".cel"));
            _resourceRegistry.ResolveResourcePath(new ResourceKey("doc.json")).Returns(Result<string>.Ok(referencerPath));

            _resourceScanner.FindReferencersAsync(sourceKey).Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(new[] { referencerKey }));

            var result = await _resourceFileSystem.MoveAsync(sourceKey, destKey);

            result.IsSuccess.Should().BeTrue();
            // Parent move completed even though the referencer was read-only.
            File.Exists(destPath).Should().BeTrue();
            // The referencer is reported in SkippedReferencers with the right reason.
            result.Value.SkippedReferencers.Should().HaveCount(1);
            result.Value.SkippedReferencers[0].Resource.Should().Be(referencerKey);
            result.Value.SkippedReferencers[0].Reason.Should().Be(ReferencerSkipReason.ReadOnly);
            result.Value.UpdatedReferencers.Should().BeEmpty();
        }
        finally
        {
            // Tear-down needs the file to be writable so the temp-folder delete works.
            if (File.Exists(referencerPath))
            {
                new FileInfo(referencerPath).IsReadOnly = false;
            }
        }
    }

    [Test]
    public async Task CreateFolderAsync_CreatesFolder_WhenAbsent()
    {
        var folder = new ResourceKey("new-folder");
        var folderPath = Path.Combine(_tempFolder, "new-folder");
        _resourceRegistry.ResolveResourcePath(folder).Returns(Result<string>.Ok(folderPath));

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_IsIdempotent_WhenFolderAlreadyExists()
    {
        var folder = new ResourceKey("existing");
        var folderPath = Path.Combine(_tempFolder, "existing");
        Directory.CreateDirectory(folderPath);
        _resourceRegistry.ResolveResourcePath(folder).Returns(Result<string>.Ok(folderPath));

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_CreatesMissingIntermediateParents()
    {
        var folder = new ResourceKey("outer/middle/inner");
        var folderPath = Path.Combine(_tempFolder, "outer", "middle", "inner");
        _resourceRegistry.ResolveResourcePath(folder).Returns(Result<string>.Ok(folderPath));

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
        _resourceRegistry.ResolveResourcePath(folder).Returns(Result<string>.Ok(folderPath));

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsFailure.Should().BeTrue();
        File.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task CreateFolderAsync_ReturnsFailure_WhenResolveFails()
    {
        var folder = new ResourceKey("bad");
        _resourceRegistry.ResolveResourcePath(folder)
            .Returns(Result<string>.Fail("simulated resolve failure"));

        var result = await _resourceFileSystem.CreateFolderAsync(folder);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task WriteAllBytesAsync_StripsReadOnlyAttribute_WhenStateIsWritable()
    {
        // The cache reports Writable, so an on-disk read-only attribute is
        // treated as stale. The write strips it and succeeds.
        var resource = new ResourceKey("writable.bin");
        var path = Path.Combine(_tempFolder, "writable.bin");
        await File.WriteAllBytesAsync(path, new byte[] { 0x00 });
        new FileInfo(path).IsReadOnly = true;

        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));
        _operationService.GetWritableStateAsync(resource)
            .Returns(Task.FromResult(WritableState.Writable));

        var bytes = new byte[] { 0xAA, 0xBB };
        var result = await _resourceFileSystem.WriteAllBytesAsync(resource, bytes);

        result.IsSuccess.Should().BeTrue();
        new FileInfo(path).IsReadOnly.Should().BeFalse();
        (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
    }

    [Test]
    public async Task WriteAllBytesAsync_PreservesReadOnlyAttribute_AndFails_WhenStateIsReadOnlyAttribute()
    {
        // The cache reports ReadOnlyAttribute, so the gate refuses to strip
        // the on-disk attribute. The underlying write throws
        // UnauthorizedAccessException, which the gateway surfaces as a
        // failure, and the read-only attribute is preserved.
        var resource = new ResourceKey("readonly.bin");
        var path = Path.Combine(_tempFolder, "readonly.bin");
        var originalBytes = new byte[] { 0x01, 0x02 };
        await File.WriteAllBytesAsync(path, originalBytes);
        new FileInfo(path).IsReadOnly = true;

        try
        {
            _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));
            _operationService.GetWritableStateAsync(resource)
                .Returns(Task.FromResult(WritableState.ReadOnlyAttribute));

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
        var path = Path.Combine(_tempFolder, "missing.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _resourceFileSystem.ReadAllBytesAsync(resource);
        stopwatch.Stop();

        result.IsFailure.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
    }
}
