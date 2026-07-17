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
    private IWorkspacePropertyBag _propertyBag = null!;
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

        _propertyBag = Substitute.For<IWorkspacePropertyBag>();
        _propertyBag.GetPropertyAsync<string>(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Sidecars.Returns(_sidecarService);

        var workspaceSettingsService = Substitute.For<IWorkspaceSettingsService>();
        workspaceSettingsService.PropertyBag.Returns(_propertyBag);
        workspaceService.WorkspaceSettings.Returns(workspaceSettingsService);

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

        editorId.Should().Be(new EditorInstanceId("test.markdown-editor"));
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
        // A stored value that is not a valid id falls through to Empty rather than throwing.
        StubExtensionPreference(".md", "not a valid id with spaces");

        var editorId = await _store.GetExtensionPreferenceAsync(".md");

        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task SetExtensionPreferenceAsync_WritesTheEditorIdString()
    {
        await _store.SetExtensionPreferenceAsync(".md", new EditorInstanceId("test.markdown-editor"));

        var expectedKey = DocumentConstants.GetEditorPreferenceKey(".md");
        await _propertyBag.Received(1).SetPropertyAsync(expectedKey, "test.markdown-editor");
    }

    [Test]
    public async Task SetExtensionPreferenceAsync_WithEmptyDeletesTheProperty()
    {
        // Passing Empty signals "clear my preference". The store removes the underlying
        // key rather than persisting an empty string that would round-trip as a malformed id.
        await _store.SetExtensionPreferenceAsync(".md", EditorInstanceId.Empty);

        var expectedKey = DocumentConstants.GetEditorPreferenceKey(".md");
        await _propertyBag.Received(1).DeletePropertyAsync(expectedKey);
        await _propertyBag.DidNotReceive().SetPropertyAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_ReturnsParsedEditorIdFromFields()
    {
        StubSidecarEditor("test.specific-editor");

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.md"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new EditorInstanceId("test.specific-editor"));
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
        // Healthy sidecar with fields but no 'editor' key means the user
        // never set a per-file preference. Treat as "no opinion", not failure.
        var content = new SidecarContent(
            new Dictionary<string, object> { ["title"] = "Notes" });
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
        // A sidecar file has no sidecar pairing of its own, so the store must not
        // call ReadAsync for one.
        _sidecarService.IsSidecarKey(Arg.Any<ResourceKey>()).Returns(true);

        var result = await _store.GetSidecarPreferenceAsync(new ResourceKey("doc.cel"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
        await _sidecarService.DidNotReceive().ReadAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task GetSidecarPreferenceAsync_SurfacesSidecarReadFailure()
    {
        // A Result.Fail from the sidecar service is an unexpected error, unlike the
        // typed NoSidecar and Broken outcomes, so it surfaces rather than being
        // treated as "no preference".
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

        editorId.Should().Be(new EditorInstanceId("test.sidecar-editor"));
    }

    [Test]
    public async Task GetPreferredEditorAsync_FallsBackToExtensionPreferenceWhenSidecarSilent()
    {
        StubExtensionPreference(".md", "test.extension-editor");

        var editorId = await _store.GetPreferredEditorAsync(new ResourceKey("doc.md"));

        editorId.Should().Be(new EditorInstanceId("test.extension-editor"));
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
        _propertyBag.GetPropertyAsync<string>(preferenceKey).Returns(Task.FromResult<string?>(editorId));
    }

    private void StubSidecarEditor(string editorId)
    {
        var fields = new Dictionary<string, object>
        {
            [SidecarFieldNames.Editor] = editorId,
        };
        var content = new SidecarContent(fields);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Healthy, content, null))));
    }
}
