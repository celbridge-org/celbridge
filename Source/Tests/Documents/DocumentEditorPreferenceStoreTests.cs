using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentEditorPreferenceStore: per-extension reads/writes against
/// workspace settings, sidecar 'editor' lookups, and the effective resolution
/// that prefers the sidecar over the per-extension preference.
/// </summary>
[TestFixture]
public class DocumentEditorPreferenceStoreTests
{
    private ISidecarService _sidecarService = null!;
    private IWorkspaceSettings _workspaceSettings = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private DocumentEditorPreferenceStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _sidecarService = Substitute.For<ISidecarService>();
        _sidecarService.IsSidecarKey(Arg.Any<ResourceKey>()).Returns(false);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null))));

        _workspaceSettings = Substitute.For<IWorkspaceSettings>();
        _workspaceSettings.GetPropertyAsync<string>(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.SidecarService.Returns(_sidecarService);
        workspaceService.WorkspaceSettings.Returns(_workspaceSettings);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _store = new DocumentEditorPreferenceStore(
            _workspaceWrapper,
            Substitute.For<ILogger<DocumentEditorPreferenceStore>>());
    }

    [Test]
    public async Task GetExtensionPreferenceAsync_ReturnsParsedEditorId()
    {
        StubExtensionPreference(".md", "test.markdown-editor");

        var editorId = await _store.GetExtensionPreferenceAsync(".md");

        editorId.Should().Be(new DocumentEditorId("test.markdown-editor"));
    }

    [Test]
    public async Task GetExtensionPreferenceAsync_ReturnsEmptyWhenNoPreference()
    {
        var editorId = await _store.GetExtensionPreferenceAsync(".md");

        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task GetExtensionPreferenceAsync_ReturnsEmptyWhenStoredValueIsMalformed()
    {
        // DocumentEditorId.TryParse rejects strings that are not a valid id;
        // a malformed value should fall through to Empty rather than throw.
        StubExtensionPreference(".md", "not a valid id with spaces");

        var editorId = await _store.GetExtensionPreferenceAsync(".md");

        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task SetExtensionPreferenceAsync_WritesTheEditorIdString()
    {
        await _store.SetExtensionPreferenceAsync(".md", new DocumentEditorId("test.markdown-editor"));

        var expectedKey = DocumentConstants.GetEditorPreferenceKey(".md");
        await _workspaceSettings.Received(1).SetPropertyAsync(expectedKey, "test.markdown-editor");
    }

    [Test]
    public async Task SetExtensionPreferenceAsync_WithEmptyDeletesTheProperty()
    {
        // Passing Empty signals "clear my preference"; the store should remove
        // the underlying key rather than persist an empty string that would
        // round-trip as a malformed id.
        await _store.SetExtensionPreferenceAsync(".md", DocumentEditorId.Empty);

        var expectedKey = DocumentConstants.GetEditorPreferenceKey(".md");
        await _workspaceSettings.Received(1).DeletePropertyAsync(expectedKey);
        await _workspaceSettings.DidNotReceive().SetPropertyAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_ReturnsParsedEditorIdFromFrontmatter()
    {
        StubSidecarEditor("test.specific-editor");

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.md"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new DocumentEditorId("test.specific-editor"));
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_ReturnsEmptyWhenNoSidecar()
    {
        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.md"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_ReturnsEmptyWhenSidecarHasNoEditorField()
    {
        // Healthy sidecar with frontmatter but no 'editor' key means the user
        // never set a per-file preference. Treat as "no opinion", not failure.
        var content = new SidecarContent(
            new Dictionary<string, object> { ["title"] = "Notes" },
            Array.Empty<SidecarBlock>());
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Healthy, content, null))));

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.md"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_ReturnsEmptyWhenEditorValueIsMalformed()
    {
        StubSidecarEditor("not a valid editor id with spaces");

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.md"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_ShortCircuitsForSidecarKey()
    {
        // The sidecar file itself does not have its own sidecar pairing; the
        // store must not call ReadAsync on a sidecar resource (which would
        // recurse pointlessly through the gateway).
        _sidecarService.IsSidecarKey(Arg.Any<ResourceKey>()).Returns(true);

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.cel"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
        await _sidecarService.DidNotReceive().ReadAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_SurfacesSidecarReadFailure()
    {
        // A read failure (not NoSidecar/Broken — those are typed outcomes, but
        // a Result.Fail from the service) is an unexpected error and should
        // surface so the caller can log it rather than be silently treated as
        // "no preference".
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Fail("read failed")));

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.md"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetPreferredEditorAsync_PrefersSidecarOverExtensionPreference()
    {
        StubSidecarEditor("test.sidecar-editor");
        StubExtensionPreference(".md", "test.extension-editor");

        var editorId = await _store.GetPreferredEditorAsync(new ResourceKey("doc.md"));

        editorId.Should().Be(new DocumentEditorId("test.sidecar-editor"));
    }

    [Test]
    public async Task GetPreferredEditorAsync_FallsBackToExtensionPreferenceWhenSidecarSilent()
    {
        StubExtensionPreference(".md", "test.extension-editor");

        var editorId = await _store.GetPreferredEditorAsync(new ResourceKey("doc.md"));

        editorId.Should().Be(new DocumentEditorId("test.extension-editor"));
    }

    [Test]
    public async Task GetPreferredEditorAsync_ReturnsEmptyWhenNeitherSourceHasPreference()
    {
        var editorId = await _store.GetPreferredEditorAsync(new ResourceKey("doc.md"));

        editorId.IsEmpty.Should().BeTrue();
    }

    private void StubExtensionPreference(string extension, string editorId)
    {
        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);
        _workspaceSettings.GetPropertyAsync<string>(preferenceKey).Returns(Task.FromResult<string?>(editorId));
    }

    private void StubSidecarEditor(string editorId)
    {
        var frontmatter = new Dictionary<string, object>
        {
            [DocumentConstants.SidecarEditorFieldName] = editorId,
        };
        var content = new SidecarContent(frontmatter, Array.Empty<SidecarBlock>());
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Healthy, content, null))));
    }
}
