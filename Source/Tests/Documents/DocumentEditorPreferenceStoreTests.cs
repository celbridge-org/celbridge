using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentEditorPreferenceStore: sidecar '_editor' lookups and the effective per-file editor
/// resolution.
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
    public async Task GetPreferredEditorAsync_ReturnsSidecarEditorWhenSet()
    {
        StubSidecarEditor("test.sidecar-editor");

        var editorId = await _store.GetPreferredEditorAsync(new ResourceKey("doc.md"));

        editorId.Should().Be(new EditorInstanceId("test.sidecar-editor"));
    }

    [Test]
    public async Task GetPreferredEditorAsync_ReturnsEmptyWhenSidecarHasNoOverride()
    {
        var editorId = await _store.GetPreferredEditorAsync(new ResourceKey("doc.md"));

        editorId.IsEmpty.Should().BeTrue();
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
