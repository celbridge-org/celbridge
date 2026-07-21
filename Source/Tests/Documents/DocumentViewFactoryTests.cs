using Celbridge.Documents.Helpers;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentViewFactory.CreateAsync across each step of the resolution chain: sidecar wins,
/// requested editor used directly, the project editor-associations map, the first factory in
/// resolution order, and the text-file fallback that prefers the code editor and skips placeholder
/// factories.
/// </summary>
[TestFixture]
public class DocumentViewFactoryTests
{
    private DocumentEditorRegistry _registry = null!;
    private ISidecarService _sidecarService = null!;
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

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Ok("c:/test/fake/path"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        resourceService.Sidecars.Returns(_sidecarService);
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

        var result = await CreateFactory().CreateAsync(new ResourceKey("missing.md"), EditorInstanceId.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_WinsOverEverythingElse()
    {
        // A registered factory exists for the extension, but the sidecar names a
        // different editor and that's the one that wins. Both factories can handle
        // the resource. Without the sidecar, the first-registered one would win.
        var sidecarEditorId = new EditorInstanceId("test.sidecar-editor");
        var sidecarView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new EditorInstanceId("test.default-editor"), ".md",
            Substitute.For<IDocumentView>());
        var sidecarFactory = CreateFakeFactory(sidecarEditorId, ".md", sidecarView);
        _registry.RegisterFactory(defaultFactory);
        _registry.RegisterFactory(sidecarFactory);

        StubSidecarEditor("test.sidecar-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sidecarView);
        result.Value.EditorId.Should().Be(sidecarEditorId);
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_FallsThroughWhenIdIsUnregistered()
    {
        // A persisted sidecar id whose package was uninstalled must not block the
        // open. Registry resolution kicks in and finds the currently-registered
        // editor for the extension.
        var defaultView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new EditorInstanceId("test.default-editor"), ".md", defaultView);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.uninstalled-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(defaultView);
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_FallsThroughWhenFactoryCannotHandleResource()
    {
        // The sidecar names an editor that's registered but its CanHandleResource
        // rejects this file. Resolution continues without losing the open.
        var rejectingFactory = CreateFakeFactory(
            new EditorInstanceId("test.rejecting"), ".md",
            Substitute.For<IDocumentView>(), canHandle: false);
        var defaultView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new EditorInstanceId("test.default"), ".md", defaultView);
        _registry.RegisterFactory(rejectingFactory);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.rejecting");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorInstanceId.Empty);

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

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.txt"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_IsUsedDirectly()
    {
        // The other factory registers first, so it would win resolution. The explicit
        // request overrides it.
        var requestedView = Substitute.For<IDocumentView>();
        var otherFactory = CreateFakeFactory(new EditorInstanceId("test.other"), ".md",
            Substitute.For<IDocumentView>());
        var requestedFactory = CreateFakeFactory(new EditorInstanceId("test.requested"), ".md", requestedView);
        _registry.RegisterFactory(otherFactory);
        _registry.RegisterFactory(requestedFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new EditorInstanceId("test.requested"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(requestedView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_FailsLoudlyWhenFactoryCannotHandle()
    {
        var requestedFactory = CreateFakeFactory(
            new EditorInstanceId("test.requested"), ".md",
            Substitute.For<IDocumentView>(), canHandle: false);
        _registry.RegisterFactory(requestedFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new EditorInstanceId("test.requested"));

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
            new EditorInstanceId("test.never-registered"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_EditorAssociations_WinOverResolutionOrder()
    {
        // No sidecar override, so the project's editor-associations map
        // decides, overriding the first-registered factory for the extension.
        var mappedView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(
            new EditorInstanceId("test.default-editor"), ".md",
            Substitute.For<IDocumentView>());
        var mappedFactory = CreateFakeFactory(new EditorInstanceId("test.mapped"), ".md", mappedView);
        _registry.RegisterFactory(defaultFactory);
        _registry.RegisterFactory(mappedFactory);

        _registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "test.mapped"
        });

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(mappedView);
    }

    [Test]
    public async Task CreateAsync_EditorAssociations_YieldToSidecarOverride()
    {
        // The sidecar names a deviation from the project default, and deviations win.
        var sidecarEditorId = new EditorInstanceId("test.sidecar-editor");
        var sidecarView = Substitute.For<IDocumentView>();
        var mappedFactory = CreateFakeFactory(
            new EditorInstanceId("test.mapped"), ".md",
            Substitute.For<IDocumentView>());
        var sidecarFactory = CreateFakeFactory(sidecarEditorId, ".md", sidecarView);
        _registry.RegisterFactory(mappedFactory);
        _registry.RegisterFactory(sidecarFactory);

        _registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "test.mapped"
        });
        StubSidecarEditor("test.sidecar-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sidecarView);
    }

    [Test]
    public async Task CreateAsync_ResolvedFactory_ChosenWhenNoPreferenceSet()
    {
        var resolvedView = Substitute.For<IDocumentView>();
        var resolvedFactory = CreateFakeFactory(
            new EditorInstanceId("test.resolved"), ".md", resolvedView);
        _registry.RegisterFactory(resolvedFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(resolvedView);
    }

    [Test]
    public async Task CreateAsync_PriorityFactory_PlaceholderIsNeverInvoked()
    {
        // Placeholder factories reserve an extension but never produce a view.
        // The resolver must skip them in the priority step (which it would
        // otherwise pick) and not call their CreateDocumentView at any point.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        var placeholderFactory = CreatePlaceholderFactory(
            new EditorInstanceId("test.placeholder"), ".xyz");
        _registry.RegisterFactory(placeholderFactory);

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".cs", codeView);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorInstanceId.Empty);

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

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    [Test]
    public async Task CreateAsync_TextFallback_FactoryScanSkipsPlaceholders()
    {
        // The text-file scan walks every registered factory. Placeholder factories
        // must not be invoked even if their CanHandleResource would accept the file.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        var placeholderFactory = CreatePlaceholderFactory(
            new EditorInstanceId("test.placeholder"), ".xyz");
        _registry.RegisterFactory(placeholderFactory);

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".cs", codeView);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorInstanceId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
        placeholderFactory.DidNotReceive().CreateDocumentView(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task CreateAsync_FailsWithUnsupportedFormatWhenSnifferReportsBinary()
    {
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(false));

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorInstanceId.Empty);

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
        var fields = new Dictionary<string, object>
        {
            [SidecarFieldNames.Editor] = editorId,
        };
        var content = new SidecarContent(fields);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Healthy, content, null))));
    }

    private static IDocumentEditorFactory CreateFakeFactory(
        EditorInstanceId editorId,
        string extension,
        IDocumentView view,
        bool canHandle = true)
    {
        // Production factories stamp view.EditorId themselves. Mocks don't, so stub it.
        view.EditorId.Returns(editorId);

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(editorId);
        factory.DisplayName.Returns(editorId.ToString());
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.IsPlaceholder.Returns(false);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        factory.CreateDocumentView(Arg.Any<ResourceKey>()).Returns(Result<IDocumentView>.Ok(view));
        return factory;
    }

    private static IDocumentEditorFactory CreatePlaceholderFactory(
        EditorInstanceId editorId,
        string extension)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(editorId);
        factory.DisplayName.Returns(editorId.ToString());
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.IsPlaceholder.Returns(true);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);
        return factory;
    }
}
