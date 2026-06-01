using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for SidecarService's dispatch between sibling-sidecar storage (regular
/// files) and self-storage (standalone .cel files), plus the idempotent-write
/// and validation behavior of the typed mutation surface. The TOML format
/// itself is covered by SidecarHelperTests; these tests assert which file gets
/// read or written and which inputs are rejected at the service boundary.
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
        workspaceService.ResourceFileSystem.Returns(_resourceFileSystem);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _sidecarService = new SidecarService(workspaceWrapper);
    }

    [Test]
    public void GetSidecarKey_FailsForCelKey()
    {
        // GetSidecarKey stays sibling-only. DeleteResourceCommand and the rename
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
        readResult.Value.Content!.Frontmatter["editor"].Should().Be("acme.binary-editor");
    }

    [Test]
    public async Task ReadAsync_ReadsFileItself_ForStandaloneCelFile()
    {
        // When the resource IS a .cel file, the file holds its own frontmatter
        // and there is no sibling sidecar. ReadAsync must operate on the file
        // directly rather than appending ".cel" again (which would produce a
        // bogus .cel.cel key).
        var standaloneCel = new ResourceKey("design.widget.cel");

        _resourceFileSystem.GetInfoAsync(standaloneCel)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(standaloneCel)
            .Returns(Task.FromResult(Result<string>.Ok("editor = \"celbridge.code-editor.code-document\"\n")));

        var readResult = await _sidecarService.ReadAsync(standaloneCel);

        readResult.IsSuccess.Should().BeTrue();
        readResult.Value.Outcome.Should().Be(SidecarReadOutcome.Healthy);
        readResult.Value.Content!.Frontmatter["editor"].Should().Be("celbridge.code-editor.code-document");

        // Belt-and-braces: the bogus .cel.cel key must never be touched.
        await _resourceFileSystem.DidNotReceive().GetInfoAsync(new ResourceKey("design.widget.cel.cel"));
        await _resourceFileSystem.DidNotReceive().ReadAllTextAsync(new ResourceKey("design.widget.cel.cel"));
    }

    [Test]
    public async Task SetFieldAsync_WritesToSiblingSidecar_ForRegularFile()
    {
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.WriteAllTextAsync(siblingSidecar, Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        var setResult = await _sidecarService.SetFieldAsync(regularFile, "editor", "acme.binary-editor");

        setResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            siblingSidecar,
            Arg.Is<string>(text => text.Contains("editor") && text.Contains("acme.binary-editor")));
    }

    [Test]
    public async Task SetFieldAsync_WritesToFileItself_ForStandaloneCelFile()
    {
        // Regression for the Open With... -> Code Editor flow on Design.fury.cel.
        // The user picks Code Editor as the per-file editor, OpenWithMenuOption
        // executes SetFieldCommand, which calls SetFieldAsync. The mutation
        // must write the "editor" field directly into the .cel file's own TOML,
        // not attempt to derive a .cel.cel sibling sidecar.
        var standaloneCel = new ResourceKey("design.widget.cel");

        _resourceFileSystem.WriteAllTextAsync(standaloneCel, Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        var setResult = await _sidecarService.SetFieldAsync(
            standaloneCel,
            "editor",
            "celbridge.code-editor.code-document");

        setResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            standaloneCel,
            Arg.Is<string>(text => text.Contains("editor")
                && text.Contains("celbridge.code-editor.code-document")));

        // The bogus .cel.cel key must never be touched.
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(
            new ResourceKey("design.widget.cel.cel"),
            Arg.Any<string>());
    }

    [Test]
    public async Task SetFieldAsync_PreservesExistingContent_ForStandaloneCelFile()
    {
        // A standalone .cel file may already carry meaningful frontmatter (e.g. a
        // fury-editor design document's [fury] section). Mutating one field must
        // preserve the rest of the frontmatter so the editor's own data survives.
        var standaloneCel = new ResourceKey("design.widget.cel");
        var existingContent = "title = \"My Design\"\nversion = 1\n";

        _resourceFileSystem.GetInfoAsync(standaloneCel)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(standaloneCel)
            .Returns(Task.FromResult(Result<string>.Ok(existingContent)));

        string? capturedWrite = null;
        _resourceFileSystem.WriteAllTextAsync(standaloneCel, Arg.Do<string>(text => capturedWrite = text))
            .Returns(Task.FromResult(Result.Ok()));

        var setResult = await _sidecarService.SetFieldAsync(
            standaloneCel,
            "editor",
            "celbridge.code-editor.code-document");

        setResult.IsSuccess.Should().BeTrue();
        capturedWrite.Should().NotBeNull();
        capturedWrite.Should().Contain("title");
        capturedWrite.Should().Contain("My Design");
        capturedWrite.Should().Contain("editor");
        capturedWrite.Should().Contain("celbridge.code-editor.code-document");
    }

    [Test]
    public async Task SetFieldAsync_SkipsWrite_WhenValueMatchesExisting()
    {
        // Idempotency: setting a field to its current value must not rewrite the
        // file. The watcher event a write would trigger fans out to a resource
        // refresh, so a redundant write is not free.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("editor = \"acme.binary-editor\"\n")));

        var setResult = await _sidecarService.SetFieldAsync(regularFile, "editor", "acme.binary-editor");

        setResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task AddTagAsync_SkipsWrite_WhenTagAlreadyPresent()
    {
        // Idempotency: AddTag with a tag already in the list is a no-op. The
        // closure inside SidecarService leaves the working dictionary unchanged,
        // and the canonical-compare must catch that so no rewrite happens.
        var regularFile = new ResourceKey("photo.png");
        var siblingSidecar = new ResourceKey("photo.png.cel");

        _resourceFileSystem.GetInfoAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(new StorageItemInfo(StorageItemKind.File, 0, default, FileSystemAttributes.None))));
        _resourceFileSystem.ReadAllTextAsync(siblingSidecar)
            .Returns(Task.FromResult(Result<string>.Ok("tags = [\"hero\", \"sprite\"]\n")));

        var addResult = await _sidecarService.AddTagAsync(regularFile, "hero");

        addResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task SetFieldAsync_RejectsNonIndexableValue()
    {
        // The frontmatter surface only accepts scalars and lists of scalars.
        // A nested dictionary (or any other unsupported shape) must fail at the
        // service boundary before any read or write happens, so the failure
        // surfaces with a clear "not indexable" message rather than from inside
        // the Tomlyn writer.
        var nested = new Dictionary<string, object> { ["nested"] = "value" };

        var setResult = await _sidecarService.SetFieldAsync(
            new ResourceKey("photo.png"),
            "metadata",
            nested);

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("not indexable");
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task WriteBlockAsync_RejectsInvalidBlockId()
    {
        // Block ids must match the dotted-lowercase rule. A bad id is caught at
        // the service boundary so the failure points at the caller's id rather
        // than at Compose's throw-on-invalid-name guard.
        var writeResult = await _sidecarService.WriteBlockAsync(
            new ResourceKey("photo.png"),
            "Invalid Block Name!",
            "body");

        writeResult.IsFailure.Should().BeTrue();
        writeResult.FirstErrorMessage.Should().Contain("block-naming rules");
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task WriteBlockAsync_RejectsContentContainingFenceLine()
    {
        // Block content that contains a line matching the fence regex would
        // cause Parse to split it incorrectly on read. The service rejects this
        // up front so the bytes never land on disk.
        var writeResult = await _sidecarService.WriteBlockAsync(
            new ResourceKey("photo.png"),
            "block-a",
            "first\n+++ \"sneaky\"\nlast\n");

        writeResult.IsFailure.Should().BeTrue();
        writeResult.FirstErrorMessage.Should().Contain("fence regex");
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
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
    public async Task SetFieldAsync_FailsForNonProjectRoot()
    {
        var setResult = await _sidecarService.SetFieldAsync(
            new ResourceKey("logs:foo.txt"),
            "editor",
            "something");

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task WriteBlockAsync_FailsForNonProjectRoot()
    {
        var writeResult = await _sidecarService.WriteBlockAsync(
            new ResourceKey("logs:foo.txt"),
            "scratch",
            string.Empty);

        writeResult.IsFailure.Should().BeTrue();
        writeResult.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task SetFieldAsync_FailsForStandaloneCelOnNonProjectRoot()
    {
        // A .cel file under logs: would, without gating, be treated as a
        // standalone-cel storage key (the same as Design.fury.cel under project:).
        // The root check must refuse it before the .cel branch.
        var setResult = await _sidecarService.SetFieldAsync(
            new ResourceKey("logs:scratch.cel"),
            "editor",
            "something");

        setResult.IsFailure.Should().BeTrue();
        setResult.FirstErrorMessage.Should().Contain("project root");
    }

    [Test]
    public async Task SetFieldAsync_CreatesFile_WhenStandaloneCelMissing()
    {
        // A standalone .cel file that does not exist yet should be created on
        // SetField. The created file holds the new frontmatter and nothing else.
        var standaloneCel = new ResourceKey("new.widget.cel");

        _resourceFileSystem.WriteAllTextAsync(standaloneCel, Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        var setResult = await _sidecarService.SetFieldAsync(
            standaloneCel,
            "editor",
            "celbridge.code-editor.code-document");

        setResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            standaloneCel,
            Arg.Is<string>(text => text.Contains("editor")));
    }

    [Test]
    public async Task RemoveFieldAsync_SkipsWrite_WhenSidecarMissing()
    {
        // Removing a field from a non-existent sidecar must not create the
        // sidecar (createIfMissing=false inside RemoveFieldAsync) and must not
        // write anything.
        var setResult = await _sidecarService.RemoveFieldAsync(new ResourceKey("photo.png"), "editor");

        setResult.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }
}
