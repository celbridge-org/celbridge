using Celbridge.Documents.Helpers;
using Celbridge.Packages;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentViewFactory.CreateAsync across each step of the resolution chain: sidecar wins,
/// requested editor used directly, the project editor-associations map, the first factory in
/// resolution order, and the text-file fallback that prefers the code editor and skips placeholder
/// factories. ResolveDefaultEditorIdAsync walks the same chain for an open that names no editor, and creates
/// no view. The code editor opens only a file in a supported format. In Safe Mode, a request for an editor
/// that is off falls through to the other routes.
/// </summary>
[TestFixture]
public class DocumentViewFactoryTests
{
    private DocumentEditorRegistry _registry = null!;
    private ISidecarService _sidecarService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private ITextBinarySniffer _textBinarySniffer = null!;
    private FileTypeHelper _fileTypeHelper = null!;
    private DocumentEditorPreferenceStore _preferenceStore = null!;
    private FileTypeClassifier _classifier = null!;

    [SetUp]
    public void Setup()
    {
        _registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        _sidecarService = Substitute.For<ISidecarService>();
        _sidecarService.IsSidecarKey(Arg.Any<ResourceKey>()).Returns(false);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null))));

        // The project folder is missing from disk, so every project key resolves to a path below it.
        var projectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(DocumentViewFactoryTests));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(projectFolder));

        var workspaceService = Substitute.For<IWorkspaceService>();
        var safeModeService = TestSafeMode.Create(isActive: false);
        workspaceService.SafeModeService.Returns(safeModeService);
        resourceService.Sidecars.Returns(_sidecarService);
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _textBinarySniffer = Substitute.For<ITextBinarySniffer>();

        // A file type that no editor claims is sniffed. Each file is text unless a test says otherwise.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        _fileTypeHelper = new FileTypeHelper();
        _fileTypeHelper.SetDocumentEditorRegistry(_registry);

        _preferenceStore = new DocumentEditorPreferenceStore(
            _workspaceWrapper,
            Substitute.For<ILogger<DocumentEditorPreferenceStore>>());

        _classifier = new FileTypeClassifier(
            _fileTypeHelper,
            _textBinarySniffer,
            _workspaceWrapper,
            _registry);
    }

    [Test]
    public async Task CreateAsync_FailsWhenResourcePathCannotBeResolved()
    {
        // The root is unregistered, so resolving the key fails.
        var result = await CreateFactory().CreateAsync(new ResourceKey("unregistered:missing.md"), EditorId.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_WinsOverEverythingElse()
    {
        // A registered factory exists for the extension, but the sidecar names a
        // different editor and that's the one that wins. Both factories can handle
        // the resource. Without the sidecar, the first-registered one would win.
        var sidecarEditorId = new EditorId("test.sidecar-editor");
        var sidecarView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new EditorId("test.default-editor"), ".md",
            Substitute.For<IDocumentView>());
        var sidecarFactory = CreateFakeFactory(sidecarEditorId, ".md", sidecarView);
        _registry.RegisterFactory(defaultFactory);
        _registry.RegisterFactory(sidecarFactory);

        StubSidecarEditor("test.sidecar-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorId.Empty);

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
        var defaultFactory = CreateFakeFactory(new EditorId("test.default-editor"), ".md", defaultView);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.uninstalled-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(defaultView);
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_FallsThroughWhenFactoryCannotHandleResource()
    {
        // The sidecar names an editor that's registered but its CanHandleResource
        // rejects this file. Resolution continues without losing the open.
        var rejectingFactory = CreateFakeFactory(
            new EditorId("test.rejecting"), ".md",
            Substitute.For<IDocumentView>(), canHandle: false);
        var defaultView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(new EditorId("test.default"), ".md", defaultView);
        _registry.RegisterFactory(rejectingFactory);
        _registry.RegisterFactory(defaultFactory);

        StubSidecarEditor("test.rejecting");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorId.Empty);

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

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.txt"), EditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_IsUsedDirectly()
    {
        // The other factory registers first, so it would win resolution. The explicit
        // request overrides it.
        var requestedView = Substitute.For<IDocumentView>();
        var otherFactory = CreateFakeFactory(new EditorId("test.other"), ".md",
            Substitute.For<IDocumentView>());
        var requestedFactory = CreateFakeFactory(new EditorId("test.requested"), ".md", requestedView);
        _registry.RegisterFactory(otherFactory);
        _registry.RegisterFactory(requestedFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new EditorId("test.requested"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(requestedView);
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_FailsLoudlyWhenFactoryCannotHandle()
    {
        var requestedFactory = CreateFakeFactory(
            new EditorId("test.requested"), ".md",
            Substitute.For<IDocumentView>(), canHandle: false);
        _registry.RegisterFactory(requestedFactory);

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new EditorId("test.requested"));

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
    public async Task CreateAsync_RequestedEditor_CodeEditorRefusesAFileInAnUnsupportedFormat()
    {
        // A binary file of an unknown type is in an unsupported format.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(false));
        _registry.RegisterFactory(CreateFakeFactory(
            DocumentConstants.CodeEditorId, ".cs", Substitute.For<IDocumentView>(), canHandle: false));

        var result = await CreateFactory().CreateAsync(
            new ResourceKey("blob.xyz"),
            DocumentConstants.CodeEditorId);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_SidecarEditor_CodeEditorIsSkippedForAFileInAnUnsupportedFormat()
    {
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(false));
        _registry.RegisterFactory(CreateFakeFactory(
            DocumentConstants.CodeEditorId, ".cs", Substitute.For<IDocumentView>(), canHandle: false));
        StubSidecarEditor(DocumentConstants.CodeEditorId.ToString());

        var factory = CreateFactory();
        var result = await factory.CreateAsync(new ResourceKey("blob.xyz"), EditorId.Empty);
        var editorId = await factory.ResolveDefaultEditorIdAsync(new ResourceKey("blob.xyz"));

        result.IsFailure.Should().BeTrue();
        editorId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_RequestedEditor_FailsWhenIdIsUnregistered()
    {
        var result = await CreateFactory().CreateAsync(
            new ResourceKey("doc.md"),
            new EditorId("test.never-registered"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CreateAsync_EditorAssociations_WinOverResolutionOrder()
    {
        // No sidecar override, so the project's editor-associations map
        // decides, overriding the first-registered factory for the extension.
        var mappedView = Substitute.For<IDocumentView>();
        var defaultFactory = CreateFakeFactory(
            new EditorId("test.default-editor"), ".md",
            Substitute.For<IDocumentView>());
        var mappedFactory = CreateFakeFactory(new EditorId("test.mapped"), ".md", mappedView);
        _registry.RegisterFactory(defaultFactory);
        _registry.RegisterFactory(mappedFactory);

        _registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "test.mapped"
        });

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(mappedView);
    }

    [Test]
    public async Task CreateAsync_EditorAssociations_YieldToSidecarOverride()
    {
        // The sidecar names a deviation from the project default, and deviations win.
        var sidecarEditorId = new EditorId("test.sidecar-editor");
        var sidecarView = Substitute.For<IDocumentView>();
        var mappedFactory = CreateFakeFactory(
            new EditorId("test.mapped"), ".md",
            Substitute.For<IDocumentView>());
        var sidecarFactory = CreateFakeFactory(sidecarEditorId, ".md", sidecarView);
        _registry.RegisterFactory(mappedFactory);
        _registry.RegisterFactory(sidecarFactory);

        _registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "test.mapped"
        });
        StubSidecarEditor("test.sidecar-editor");

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sidecarView);
    }

    [Test]
    public async Task CreateAsync_ResolvedFactory_ChosenWhenNoPreferenceSet()
    {
        var resolvedView = Substitute.For<IDocumentView>();
        var resolvedFactory = CreateFakeFactory(
            new EditorId("test.resolved"), ".md", resolvedView);
        _registry.RegisterFactory(resolvedFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.md"), EditorId.Empty);

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
            new EditorId("test.placeholder"), ".xyz");
        _registry.RegisterFactory(placeholderFactory);

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".cs", codeView);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorId.Empty);

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

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorId.Empty);

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
            new EditorId("test.placeholder"), ".xyz");
        _registry.RegisterFactory(placeholderFactory);

        var codeView = Substitute.For<IDocumentView>();
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".cs", codeView);
        _registry.RegisterFactory(codeFactory);

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorId.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
        placeholderFactory.DidNotReceive().CreateDocumentView(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task CreateAsync_FailsWithUnsupportedFormatWhenSnifferReportsBinary()
    {
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(false));

        var result = await CreateFactory().CreateAsync(new ResourceKey("doc.xyz"), EditorId.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task ResolveDefaultEditorIdAsync_SidecarEditor_WinsOverEverythingElse()
    {
        var sidecarEditorId = new EditorId("test.sidecar-editor");
        var defaultFactory = CreateFakeFactory(
            new EditorId("test.default-editor"), ".md", Substitute.For<IDocumentView>());
        var sidecarFactory = CreateFakeFactory(sidecarEditorId, ".md", Substitute.For<IDocumentView>());
        _registry.RegisterFactory(defaultFactory);
        _registry.RegisterFactory(sidecarFactory);

        StubSidecarEditor("test.sidecar-editor");

        var editorId = await CreateFactory().ResolveDefaultEditorIdAsync(new ResourceKey("doc.md"));

        editorId.Should().Be(sidecarEditorId);
    }

    [Test]
    public async Task ResolveDefaultEditorIdAsync_FollowsTheFileExtension()
    {
        // A rename across extensions resolves to the editor that claims the new name.
        var consoleEditorId = new EditorId("test.console");
        var consoleFactory = CreateFakeFactory(consoleEditorId, ".console", Substitute.For<IDocumentView>());
        var codeFactory = CreateFakeFactory(DocumentConstants.CodeEditorId, ".txt", Substitute.For<IDocumentView>());
        _registry.RegisterFactory(consoleFactory);
        _registry.RegisterFactory(codeFactory);

        var factory = CreateFactory();
        var consoleResolved = await factory.ResolveDefaultEditorIdAsync(new ResourceKey("doc.console"));
        var textResolved = await factory.ResolveDefaultEditorIdAsync(new ResourceKey("doc.txt"));

        consoleResolved.Should().Be(consoleEditorId);
        textResolved.Should().Be(DocumentConstants.CodeEditorId);
    }

    [Test]
    public async Task ResolveDefaultEditorIdAsync_TextFallback_IsTheCodeEditor()
    {
        // Unknown extension, sniffer reports text, no factory claims it.
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(true));

        var codeFactory = CreateFakeFactory(
            DocumentConstants.CodeEditorId, ".cs", Substitute.For<IDocumentView>(), canHandle: false);
        _registry.RegisterFactory(codeFactory);

        var editorId = await CreateFactory().ResolveDefaultEditorIdAsync(new ResourceKey("doc.xyz"));

        editorId.Should().Be(DocumentConstants.CodeEditorId);
    }

    [Test]
    public async Task ResolveDefaultEditorIdAsync_IsEmptyWhenNoEditorCanOpenTheFile()
    {
        _textBinarySniffer.IsTextFile(Arg.Any<string>()).Returns(Result<bool>.Ok(false));

        var editorId = await CreateFactory().ResolveDefaultEditorIdAsync(new ResourceKey("doc.xyz"));

        editorId.IsEmpty.Should().BeTrue();
    }

    [TestCase("run.console", "celbridge-console.console")]
    [TestCase("page.html", "celbridge.html")]
    [TestCase("readme.md", "celbridge.markdown")]
    [TestCase("links.browser", "celbridge.browser-editor")]
    public async Task InSafeMode_ARequestForAnEditorThatIsOff_FallsThroughToTheCodeEditor(string fileName, string editorId)
    {
        var codeView = TurnOnSafeMode();

        var result = await CreateFactory().CreateAsync(new ResourceKey(fileName), new EditorId(editorId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(codeView);
    }

    // Turns Safe Mode on, and registers the code editor alone. In Safe Mode, the other editors never register.
    // Returns the code editor's view.
    private IDocumentView TurnOnSafeMode()
    {
        var safeModeService = TestSafeMode.Create(isActive: true);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        var codeView = Substitute.For<IDocumentView>();
        _registry.RegisterFactory(CreateFakeFactory(DocumentConstants.CodeEditorId, ".txt", codeView));

        return codeView;
    }

    private DocumentViewFactory CreateFactory()
    {
        return new DocumentViewFactory(
            _registry,
            _workspaceWrapper,
            _preferenceStore,
            _classifier,
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
        EditorId editorId,
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
        EditorId editorId,
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
