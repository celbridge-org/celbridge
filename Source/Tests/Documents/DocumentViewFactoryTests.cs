using Celbridge.Documents.Helpers;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentViewFactory.CreateAsync across each step of the
/// resolution chain: sidecar wins, requested editor used directly, workspace
/// preference, priority-based factory, and the text-file fallback that prefers
/// the code editor and skips placeholder factories.
/// </summary>
[TestFixture]
public class DocumentViewFactoryTests
{
    private DocumentEditorRegistry _registry = null!;
    private ISidecarService _sidecarService = null!;
    private IWorkspaceSettings _workspaceSettings = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private ITextBinarySniffer _textBinarySniffer = null!;
    private FileTypeHelper _fileTypeHelper = null!;
    private DocumentEditorPreferenceStore _preferenceStore = null!;
    private FileTypeClassifier _classifier = null!;
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        _registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        _sidecarService = Substitute.For<ISidecarService>();
        _sidecarService.IsSidecarKey(Arg.Any<ResourceKey>()).Returns(false);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null))));

        _workspaceSettings = Substitute.For<IWorkspaceSettings>();
        _workspaceSettings.GetPropertyAsync<string>(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Ok("c:/test/fake/path"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        resourceService.Sidecars.Returns(_sidecarService);
        workspaceService.WorkspaceSettings.Returns(_workspaceSettings);
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _textBinarySniffer = Substitute.For<ITextBinarySniffer>();

        _fileTypeHelper = new FileTypeHelper();
        _fileTypeHelper.SetDocumentEditorRegistry(_registry);
        _fileTypeHelper.Initialize();

        _preferenceStore = new DocumentEditorPreferenceStore(
            _workspaceWrapper,
            Substitute.For<ILogger<DocumentEditorPreferenceStore>>());

        _classifier = new FileTypeClassifier(
            _fileTypeHelper,
            _textBinarySniffer,
            _workspaceWrapper,
            _registry);

        _serviceProvider = Substitute.For<IServiceProvider>();
    }

    [Test]
    public async Task CreateAsync_FailsWhenResourcePathCannotBeResolved()
    {
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Fail("missing"));

        var result = await CreateFactory().CreateAsync(new ResourceKey("missing.md"), DocumentEditorId.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_WinsOverEverythingElse()
    {
        // A registered factory exists for the extension, but the sidecar names a
        // different editor and that's the one that wins. Both factories can
        // handle the resource; without the sidecar, priority would pick the
        // specialized one.
        var sidecarEditorId = new DocumentEditorId("test.sidecar-editor");
        var sidecarView = Substitute.For<IDocumentView>();
        var sidecarFactory = CreateFakeFactory(sidecarEditorId, ".md", sidecarView, EditorPriority.General);
        var defaultFactory = CreateFakeFactory(new DocumentEditorId("test.default-editor"), ".md",
            Substitute.For<IDocumentView>(), EditorPriority.Specialized);
        _registry.RegisterFactory(sidecarFactory);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.sidecar-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sidecarView);
        result.Value.EditorId.Should().Be(sidecarEditorId);
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_FallsThroughWhenIdIsUnregistered()
    {
        // A persisted sidecar id whose package was uninstalled must not block
        // the open; the priority-based resolution kicks in and finds the
        // currently-registered editor for the extension.
        var defaultView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new DocumentEditorId("test.default-editor"), ".md", defaultView);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.uninstalled-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(defaultView);
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_FallsThroughWhenFactoryCannotHandleResource()
    {
        // The sidecar names an editor that's registered but its CanHandleResource
        // rejects this file. Resolution continues without losing the open.
        var rejectingFactory = CreateFakeFactory(
            new DocumentEditorId("test.rejecting"), ".md",
            Substitute.For<IDocumentView>(), canHandle: false);
        var defaultView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new DocumentEditorId("test.default"), ".md", defaultView);
        _registry.RegisterFactory(rejectingFactory);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.rejecting");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(defaultView);
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_CodeEditorIsAcceptedRegardlessOfExtensionClaim()
    {
        // The code editor is the universal "view as text" choice. Its
        // CanHandleResource is keyed to its extension list, so the resolver
        // bypasses that check when the sidecar names the code editor id.
        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(
            DocumentConstants.CodeEditorId, ".cs", codeView, canHandle: false);
        _registry.RegisterFactory(codeFactory);

        StubSidecarEditor(DocumentConstants.CodeEditorId.ToString());

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.txt"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_IsUsedDirectly()
    {
        var requestedView = Substitute.For<IDocumentView>();
        var requestedFactory = CreateFakeFactory(new DocumentEditorId("test.requested"), ".md", requestedView);
        var otherFactory = CreateFakeFactory(new DocumentEditorId("test.other"), ".md",
            Substitute.For<IDocumentView>(), EditorPriority.Specialized);
        _registry.RegisterFactory(requestedFactory);
        _registry.RegisterFactory(otherFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new DocumentEditorId("test.requested"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(requestedView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_FailsLoudlyWhenFactoryCannotHandle()
    {
        // Failing to honour an explicit caller request would hide bugs like
        // an MCP document_open call passing the wrong extension to a tool.
        var requestedFactory = CreateFakeFactory(
            new DocumentEditorId("test.requested"), ".md",
            Substitute.For<IDocumentView>(), canHandle: false);
        _registry.RegisterFactory(requestedFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new DocumentEditorId("test.requested"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_CodeEditorBypassesExtensionCheck()
    {
        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(
            DocumentConstants.CodeEditorId, ".cs", codeView, canHandle: false);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.txt"),
            DocumentConstants.CodeEditorId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_FailsWhenIdIsUnregistered()
    {
        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new DocumentEditorId("test.never-registered"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_WorkspacePreference_PicksConfiguredEditor()
    {
        // No sidecar, no explicit request, but the workspace preference for
        // this extension points at a non-specialized editor that should win
        // over the priority-default.
        var preferredView = Substitute.For<IDocumentView>();
        var preferredFactory = CreateFakeFactory(
            new DocumentEditorId("test.preferred"), ".md", preferredView, EditorPriority.General);
        var specializedFactory = CreateFakeFactory(
            new DocumentEditorId("test.specialized"), ".md",
            Substitute.For<IDocumentView>(), EditorPriority.Specialized);
        _registry.RegisterFactory(preferredFactory);
        _registry.RegisterFactory(specializedFactory);

        StubExtensionPreference(".md", "test.preferred");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(preferredView);
    }

    [Test]
    public async Task CreateAsync_PriorityFactory_ChosenWhenNoPreferenceSet()
    {
        var specializedView = Substitute.For<IDocumentView>();
        var specializedFactory = CreateFakeFactory(
            new DocumentEditorId("test.specialized"), ".md", specializedView, EditorPriority.Specialized);
        _registry.RegisterFactory(specializedFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(specializedView);
    }

    [Test]
    public async Task CreateAsync_PriorityFactory_PlaceholderIsNeverInvoked()
    {
        // Placeholder factories reserve an extension but never produce a view.
        // The resolver must skip them in the priority step (which it would
        // otherwise pick) and not call their CreateDocumentView at any point.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        var placeholderFactory = CreatePlaceholderFactory(
            new DocumentEditorId("test.placeholder"), ".xyz");
        _registry.RegisterFactory(placeholderFactory);

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".cs", codeView);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
        placeholderFactory.DidNotReceive().CreateDocumentView(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task CreateAsync_TextFallback_PrefersCodeEditorForUnknownTextExtensions()
    {
        // Unknown extension, sniffer reports text, no factory claims it.
        // Resolver should route to the code editor's id even though its
        // CanHandleResource may reject the extension.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(
            DocumentConstants.CodeEditorId, ".cs", codeView, canHandle: false);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    [Test]
    public async Task CreateAsync_TextFallback_FactoryScanSkipsPlaceholders()
    {
        // The text-file scan walks every registered factory; placeholder
        // factories must not be invoked even if their CanHandleResource would
        // accept the file.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        var placeholderFactory = CreatePlaceholderFactory(
            new DocumentEditorId("test.placeholder"), ".xyz");
        _registry.RegisterFactory(placeholderFactory);

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".cs", codeView);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), DocumentEditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
        placeholderFactory.DidNotReceive().CreateDocumentView(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task CreateAsync_FailsWithUnsupportedFormatWhenSnifferReportsBinary()
    {
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(false));

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), DocumentEditorId.Empty);

        result.IsFailure.Should().BeTrue();
    }

    private DocumentViewFactory CreateFactory()
    {
        return new DocumentViewFactory(
            _registry,
            _workspaceWrapper,
            _preferenceStore,
            _classifier,
            _serviceProvider,
            Substitute.For<ILogger<DocumentViewFactory>>());
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

    private void StubExtensionPreference(string extension, string editorId)
    {
        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);
        _workspaceSettings.GetPropertyAsync<string>(preferenceKey).Returns(Task.FromResult<string?>(editorId));
    }

    private static IDocumentEditorFactory CreateFakeFactory(
        DocumentEditorId editorId,
        string extension,
        IDocumentView view,
        EditorPriority priority = EditorPriority.Specialized,
        bool canHandle = true)
    {
        // Production factories stamp view.EditorId themselves; mocks don't, so stub it.
        view.EditorId.Returns(editorId);

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(editorId);
        factory.DisplayName.Returns(editorId.ToString());
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.Priority.Returns(priority);
        factory.IsPlaceholder.Returns(false);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        factory.CreateDocumentView(Arg.Any<ResourceKey>()).Returns(Result<IDocumentView>.Ok(view));
        return factory;
    }

    private static IDocumentEditorFactory CreatePlaceholderFactory(
        DocumentEditorId editorId,
        string extension)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(editorId);
        factory.DisplayName.Returns(editorId.ToString());
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.Priority.Returns(EditorPriority.General);
        factory.IsPlaceholder.Returns(true);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);
        return factory;
    }
}
