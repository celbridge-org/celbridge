using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for SidecarService's read-modify-write engine: which file gets read
/// or written for a given resource key, the idempotent-write contract, the
/// orphan-prevention check that refuses to create a sidecar when the parent
/// file is absent, and the validation behavior of the typed batch mutation
/// surface. The TOML format itself is covered by SidecarHelperTests; these
/// tests assert service-boundary behavior.
/// </summary>
[TestFixture]
public class SidecarServiceTests
{
    private IResourceFileSystem _resourceFileSystem = null!;
    private SidecarService _sidecarService = null!;

    [SetUp]
    public void Setup()
    {
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        // Default: nothing exists on disk. Tests opt-in per resource.
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.NotFound, 0, default, FileSystemAttributes.None))));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _sidecarService = new SidecarService(workspaceWrapper);
    }

    [Test]
    public void GetSidecarKey_FailsForCelKey()
    {
        // GetSidecarKey stays parent-only. DeleteResourceCommand and the rename
        // cascade rely on this failure to skip the "also delete/rename the
        // sidecar" code path when the resource is itself a .cel file.
        var result = _sidecarService.GetSidecarKey(new ResourceKey("design.widget.cel"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("pass the parent resource key instead");
    }

    [Test]
    public async Task ReadAsync_ReadsSiblingSidecar_ForRegularFile()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("editor = \"acme.binary-editor\"\n")));

        var readResult = await _sidecarService.ReadAsync(regularFile);

        readResult.IsSuccess.Should().BeTrue();
        readResult.Value.Outcome.Should().Be(SidecarReadOutcome.Healthy);
        readResult.Value.Content!.Fields["editor"].Should().Be("acme.binary-editor");
    }

    [Test]
    public async Task ReadAsync_ReadsFileItself_WhenResourceIsCelKey()
    {
        // Internal callers that already hold a .cel key (e.g. the open-cel
        // feature flag's open path) read the file's own content directly
        // rather than appending .cel again (which would produce a bogus
        // .cel.cel key).
        var celResource = new ResourceKey("design.widget.cel");

        _resourceFileSystem.GetInfoAsync(celResource)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(celResource)
            .Returns(Task.FromResult(Result<string>.Ok("editor = \"celbridge.code-editor.code-document\"\n")));

        var readResult = await _sidecarService.ReadAsync(celResource);

        readResult.IsSuccess.Should().BeTrue();
        readResult.Value.Outcome.Should().Be(SidecarReadOutcome.Healthy);
        readResult.Value.Content!.Fields["editor"].Should().Be("celbridge.code-editor.code-document");

        // Belt-and-braces: the bogus .cel.cel key must never be touched.
        await _resourceFileSystem.DidNotReceive().GetInfoAsync(new ResourceKey("design.widget.cel.cel"));
        await _resourceFileSystem.DidNotReceive().ReadAllTextAsync(new ResourceKey("design.widget.cel.cel"));
    }

    [Test]
    public async Task SetFieldsAsync_WritesToSiblingSidecar_ForRegularFile()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        // The parent file must exist on disk; otherwise the orphan-prevention
        // check inside MutateFieldsAsync refuses to create the new sidecar.
        _resourceFileSystem.GetInfoAsync(regularFile)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["editor"] = "acme.binary-editor",
        };
        var setResult = await _sidecarService.SetFieldsAsync(regularFile, fields);

        setResult.IsSuccess.Should().BeTrue();
        // The sidecar did not exist on disk, so this write materialises a new
        // file. The Created outcome is what gates the registry-update flag on
        // the calling command (existing-file updates must report Updated so
        // the rescan is skipped).
        setResult.Value.Should().Be(SidecarWriteOutcome.Created);
        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            siblingSidecar,
            Arg.Is<string>(text => text.Contains("editor") && text.Contains("acme.binary-editor")));
    }

    [Test]
    public async Task SetFieldsAsync_ReportsUpdated_WhenSidecarAlreadyExists()
    {
        // Mutating an existing sidecar reports Updated, not Created. The
        // command layer uses this distinction to suppress the synchronous
        // registry rebuild that's only needed when a new .cel file appears.
        var celResource = new ResourceKey("design.widget.cel");

        _resourceFileSystem.GetInfoAsync(celResource)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(celResource)
            .Returns(Task.FromResult(Result<string>.Ok("title = \"old\"\n")));
        _resourceFileSystem.WriteAllTextAsync(celResource, Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["title"] = "new",
        };
        var setResult = await _sidecarService.SetFieldsAsync(celResource, fields);

        setResult.IsSuccess.Should().BeTrue();
        setResult.Value.Should().Be(SidecarWriteOutcome.Updated);
    }

    [Test]
    public async Task SetFieldsAsync_ReportsNoChange_WhenValuesMatchExisting()
    {
        // The canonical-compare short-circuits a redundant write. The outcome
        // must be NoChange so the command layer can skip both the watcher
        // fan-out and the registry rebuild for set-to-current-value calls.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("editor = \"acme.binary-editor\"\n")));

        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["editor"] = "acme.binary-editor",
        };
        var setResult = await _sidecarService.SetFieldsAsync(regularFile, fields);

        setResult.IsSuccess.Should().BeTrue();
        setResult.Value.Should().Be(SidecarWriteOutcome.NoChange);
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task SetFieldsAsync_PreservesExistingContent_OnExistingSidecar()
    {
        // An existing sidecar may already carry meaningful fields. Mutating
        // one field must preserve the rest of the fields so user data
        // survives the round-trip.
        var celResource = new ResourceKey("design.widget.cel");
        var existingContent = "title = \"My Design\"\nversion = 1\n";

        _resourceFileSystem.GetInfoAsync(celResource)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(celResource)
            .Returns(Task.FromResult(Result<string>.Ok(existingContent)));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(celResource, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["editor"] = "celbridge.code-editor.code-document",
        };
        var setResult = await _sidecarService.SetFieldsAsync(celResource, fields);

        setResult.IsSuccess.Should().BeTrue();
        capturedWrite.Should().NotBeNull();
        capturedWrite.Should().Contain("title");
        capturedWrite.Should().Contain("My Design");
        capturedWrite.Should().Contain("editor");
        capturedWrite.Should().Contain("celbridge.code-editor.code-document");
    }

    [Test]
    public async Task SetFieldsAsync_AppliesBatchAtomically_OneWrite()
    {
        // The batch contract: one read, one in-memory mutate, one write. A
        // batch of N fields must produce exactly one WriteAllTextAsync call,
        // and the resulting file must carry every field.
        var celResource = new ResourceKey("design.widget.cel");

        _resourceFileSystem.GetInfoAsync(celResource)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(celResource)
            .Returns(Task.FromResult(Result<string>.Ok(string.Empty)));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(celResource, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["title"] = "Hello",
            ["count"] = 42L,
            ["enabled"] = true,
        };

        var setResult = await _sidecarService.SetFieldsAsync(celResource, fields);

        setResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(celResource, Arg.Any<string>());
        capturedWrite.Should().Contain("title");
        capturedWrite.Should().Contain("Hello");
        capturedWrite.Should().Contain("count = 42");
        capturedWrite.Should().Contain("enabled = true");
    }

    [Test]
    public async Task SetFieldsAsync_FailsWholeBatch_WhenAnyValueIsNotIndexable()
    {
        // Atomicity for invalid input: a batch with one bad value must fail
        // the whole call and not write anything. Validation runs before the
        // read-modify-write.
        var regularFile = new ResourceKey("photo.png");

        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["valid"] = "ok",
            ["invalid"] = new Dictionary<string, object> { ["nested"] = "value" },
        };

        var setResult = await _sidecarService.SetFieldsAsync(regularFile, fields);

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("invalid");
        setResult.FirstErrorMessage.Should().Contain("not indexable");
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task AddTagsAsync_AppendsBatch_InOneWrite()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(regularFile)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var addResult = await _sidecarService.AddTagsAsync(regularFile, new[] { "hero", "sprite", "draft" });

        addResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(siblingSidecar, Arg.Any<string>());
        capturedWrite.Should().Contain("_tags");
        capturedWrite.Should().Contain("hero");
        capturedWrite.Should().Contain("sprite");
        capturedWrite.Should().Contain("draft");
    }

    [Test]
    public async Task AddTagsAsync_SkipsWrite_WhenAllTagsAlreadyPresent()
    {
        // Idempotency: a batch where every entry is already in the list is a
        // no-op. The canonical-compare must catch it so no rewrite happens.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"hero\", \"sprite\"]\n")));

        var addResult = await _sidecarService.AddTagsAsync(regularFile, new[] { "hero", "sprite" });

        addResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task AddTagsAsync_OrderDoesNotMatter_FinalSetIsUnion()
    {
        // Set-union semantics: calling with the same tags in any order yields
        // the same on-disk encoding. Tested via the canonical-compare path —
        // writing a permutation after the same set is on disk produces NoChange.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"a\", \"b\", \"c\"]\n")));

        var addResult = await _sidecarService.AddTagsAsync(regularFile, new[] { "c", "a", "b" });

        addResult.IsSuccess.Should().BeTrue();
        addResult.Value.Should().Be(SidecarWriteOutcome.NoChange);
    }

    [Test]
    public async Task RemoveTagsAsync_DropsBatch_InOneWrite()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"hero\", \"sprite\", \"draft\"]\n")));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var removeResult = await _sidecarService.RemoveTagsAsync(regularFile, new[] { "sprite", "draft" });

        removeResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(siblingSidecar, Arg.Any<string>());
        capturedWrite.Should().Contain("hero");
        capturedWrite.Should().NotContain("sprite");
        capturedWrite.Should().NotContain("draft");
    }

    [Test]
    public async Task RemoveTagsAsync_DropsTagsField_WhenListBecomesEmpty()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"hero\", \"sprite\"]\ntitle = \"keep\"\n")));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var removeResult = await _sidecarService.RemoveTagsAsync(regularFile, new[] { "hero", "sprite" });

        removeResult.IsSuccess.Should().BeTrue();
        capturedWrite.Should().NotContain("_tags");
        capturedWrite.Should().Contain("title");
    }

    [Test]
    public async Task RemoveTagsAsync_SkipsWrite_WhenNoTagsPresent()
    {
        // Idempotency: removing tags none of which are present is a no-op.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("_tags = [\"hero\"]\n")));

        var removeResult = await _sidecarService.RemoveTagsAsync(regularFile, new[] { "ghost", "phantom" });

        removeResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task RemoveFieldsAsync_RemovesBatch_InOneWrite()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("title = \"x\"\nauthor = \"a\"\nversion = 2\n")));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var removeResult = await _sidecarService.RemoveFieldsAsync(regularFile, new[] { "title", "author" });

        removeResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(siblingSidecar, Arg.Any<string>());
        capturedWrite.Should().NotContain("title");
        capturedWrite.Should().NotContain("author");
        capturedWrite.Should().Contain("version");
    }

    [Test]
    public async Task RemoveFieldsAsync_SkipsWrite_WhenSidecarMissing()
    {
        // Removing fields from a non-existent sidecar must not create the
        // sidecar (createIfMissing=false inside RemoveFieldsAsync) and must
        // not write anything.
        var removeResult = await _sidecarService.RemoveFieldsAsync(new ResourceKey("photo.png"), new[] { "title" });

        removeResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task RemoveFieldsAsync_SilentNoOpForMissingNames()
    {
        // Names that are not present on the sidecar are silent no-ops. The
        // mix of present and absent names still writes once for the present
        // entry.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("title = \"x\"\n")));
        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        var removeResult = await _sidecarService.RemoveFieldsAsync(regularFile, new[] { "title", "nonexistent" });

        removeResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(siblingSidecar, Arg.Any<string>());
    }

    [Test]
    public void GetSidecarKey_FailsForNonProjectRoot()
    {
        // Sidecars are a project-scoped metadata system; the tracking pass only
        // scans the project tree, so cross-root sidecars would be silently
        // invisible to validation. The API refuses non-project roots up front.
        var result = _sidecarService.GetSidecarKey(new ResourceKey("logs:foo.txt"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task ReadAsync_FailsForNonProjectRoot()
    {
        var readResult = await _sidecarService.ReadAsync(new ResourceKey("logs:foo.txt"));

        readResult.IsFailure.Should().BeTrue();
        readResult.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task SetFieldsAsync_FailsForNonProjectRoot()
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["editor"] = "something",
        };
        var setResult = await _sidecarService.SetFieldsAsync(new ResourceKey("logs:foo.txt"), fields);

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task SetFieldsAsync_FailsForCelKeyOnNonProjectRoot()
    {
        // A .cel file under logs: would, without gating, hit the .cel-as-self
        // branch. The root check must refuse it before any .cel-specific
        // dispatch.
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["editor"] = "something",
        };
        var setResult = await _sidecarService.SetFieldsAsync(new ResourceKey("logs:scratch.cel"), fields);

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task AddTagsAsync_RefusesToCreate_WhenParentFileDoesNotExist()
    {
        // Orphan-prevention: creating a sidecar when neither the sidecar nor
        // the parent file exists would materialise an orphan .cel on disk. The
        // mutator refuses, telling the caller to pass a parent key that maps
        // to an actual file.
        var phantomParent = new ResourceKey("sprite");

        // Default GetInfoAsync setup returns NotFound for everything.

        var addResult = await _sidecarService.AddTagsAsync(phantomParent, new[] { "hero" });

        addResult.IsFailure.Should().BeTrue();
        addResult.FirstErrorMessage.Should().Contain("parent file");
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task SetFieldsAsync_RefusesToCreate_WhenParentFileDoesNotExist()
    {
        // Same orphan-prevention as AddTagsAsync but for SetFields.
        var phantomParent = new ResourceKey("ghost");
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["editor"] = "celbridge.code-editor.code-document",
        };

        var setResult = await _sidecarService.SetFieldsAsync(phantomParent, fields);

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("parent file");
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }
}
