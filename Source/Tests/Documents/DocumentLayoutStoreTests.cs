using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentLayoutStore: restore-parsing edge cases (corrupted layout,
/// invalid resource keys, section clamps), the default-readme fallback when no
/// layout is stored, and the basic settings-writing shape of the Store* methods.
/// </summary>
[TestFixture]
public class DocumentLayoutStoreTests
{
    private IWorkspacePropertyBag _propertyBag = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IDocumentsPanel _documentsPanel = null!;
    private IUtilityService _utilityService = null!;
    private ICommandService _commandService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private DocumentLayoutStore _store = null!;
    private string _tempFolder = null!;
    private string _accessibleFilePath = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(DocumentLayoutStoreTests));
        Directory.CreateDirectory(_tempFolder);
        _accessibleFilePath = Path.Combine(_tempFolder, "accessible.md");
        File.WriteAllText(_accessibleFilePath, string.Empty);

        _propertyBag = Substitute.For<IWorkspacePropertyBag>();
        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        _documentsPanel = Substitute.For<IDocumentsPanel>();
        _commandService = Substitute.For<ICommandService>();

        // Default registry behaviour: every key resolves to the accessible temp
        // file and exists in the registry. Individual tests override these
        // when they want to exercise the negative branches.
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Ok(_accessibleFilePath));
        _resourceRegistry.GetResource(Arg.Any<ResourceKey>())
            .Returns(Result<IResource>.Ok(Substitute.For<IResource>()));

        _documentsPanel.OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>())
            .Returns(Task.FromResult(Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Opened)));
        _documentsPanel.SectionCount.Returns(1);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();

        var workspaceSettingsService = Substitute.For<IWorkspaceSettingsService>();
        workspaceSettingsService.PropertyBag.Returns(_propertyBag);
        workspaceService.WorkspaceSettings.Returns(workspaceSettingsService);
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());
        workspaceService.DocumentsPanel.Returns(_documentsPanel);

        // A stored utils: entry drives the dock mechanism through the utility service. Default it to success.
        _utilityService = Substitute.For<IUtilityService>();
        _utilityService.RestoreDockedUtility(Arg.Any<ResourceKey>(), Arg.Any<DocumentAddress>())
            .Returns(Result.Ok());
        workspaceService.UtilityService.Returns(_utilityService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // Wire a real LocalResourceFileSystem so GetInfoAsync probes the actual disk
        // paths the registry resolves to.
        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        _store = new DocumentLayoutStore(
            _workspaceWrapper,
            _commandService,
            Substitute.For<ILogger<DocumentLayoutStore>>());
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
    public async Task RestorePanelStateAsync_NoStoredLayout_OpensDefaultReadme()
    {
        // Empty workspace: settings has no layout key, so we fall back to
        // opening readme.md if it resolves and is readable.
        _resourceRegistry.NormalizeResourceKey(Arg.Any<ResourceKey>())
            .Returns(ci => Result<ResourceKey>.Ok(ci.Arg<ResourceKey>()));

        await _store.RestorePanelStateAsync();

        // ICommandService.Execute has [CallerFilePath]/[CallerLineNumber]
        // parameters that the compiler fills in at each call site, so the
        // verification must accept any value for those.
        _commandService.Received(1).Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task RestorePanelStateAsync_NoStoredLayout_SkipsReadmeWhenItDoesNotResolve()
    {
        // No readme.md in the workspace: NormalizeResourceKey fails and the
        // fallback is a no-op rather than an error.
        _resourceRegistry.NormalizeResourceKey(Arg.Any<ResourceKey>())
            .Returns(Result<ResourceKey>.Fail("not found"));

        await _store.RestorePanelStateAsync();

        _commandService.DidNotReceive().Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task RestorePanelStateAsync_MalformedLayoutJson_DoesNotThrow()
    {
        // Old format / corrupted settings: GetPropertyAsync throws inside the
        // store, which catches and treats the layout as empty.
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns<Task<List<DocumentLayoutStore.StoredDocumentAddress>?>>(_ => throw new InvalidOperationException("bad json"));
        _resourceRegistry.NormalizeResourceKey(Arg.Any<ResourceKey>())
            .Returns(Result<ResourceKey>.Fail("not found"));

        Func<Task> act = async () => await _store.RestorePanelStateAsync();

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task RestorePanelStateAsync_RestoresStoredAddressesViaPanelOpen()
    {
        // One stored doc: the store should call panel.OpenDocument with an
        // empty editor id (sidecar wins at restore) and the saved address.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", WindowIndex: 0, SectionIndex: 0, TabOrder: 2),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync();

        await _documentsPanel.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.EditorId == EditorId.Empty
                && options.Activate == false
                && options.Address!.SectionIndex == 0
                && options.Address.TabOrder == 2));
    }

    [Test]
    public async Task RestorePanelStateAsync_InvalidResourceKey_IsSkipped()
    {
        // A stored address whose Resource string isn't a valid ResourceKey
        // must not abort the rest of the restore.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("///invalid///", 0, 0, 0),
            new("notes/readme.md", 0, 0, 1),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync();

        await _documentsPanel.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Any<OpenDocumentOptions>());
        await _documentsPanel.Received(1).OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_MissingResource_IsSkipped()
    {
        // The resource key is well-formed but no longer exists in the registry
        // (e.g., the file was deleted between sessions). Skip without failing.
        _resourceRegistry.GetResource(new ResourceKey("notes/readme.md"))
            .Returns(Result<IResource>.Fail("missing"));
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, 0, 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync();

        await _documentsPanel.DidNotReceive().OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_UtilityResource_DocksViaDocumentsService()
    {
        // A utils: resource is a utility, a permanent Utility Panel surface instantiated eagerly at load. A
        // stored utils: entry means it was docked last session, so the restore drives the dock mechanism to
        // reparent the already-live utility into its saved position rather than opening a second document.
        var utilityResource = new ResourceKey("utils:settings._notepad");
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new(utilityResource.ToString(), WindowIndex: 0, SectionIndex: 0, TabOrder: 3),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync();

        await _utilityService.Received(1).RestoreDockedUtility(
            utilityResource,
            Arg.Is<DocumentAddress>(address => address.SectionIndex == 0 && address.TabOrder == 3));
        await _documentsPanel.DidNotReceive().OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_InaccessibleFile_IsSkipped()
    {
        // ResolveResourcePath returns a path that does not exist on disk, so
        // ResourceFileSystem.GetInfoAsync reports NotFound and the restore skips.
        var missingPath = Path.Combine(_tempFolder, "does_not_exist.md");
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Ok(missingPath));
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, 0, 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync();

        await _documentsPanel.DidNotReceive().OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_SectionIndexLargerThanCount_ClampsToLastSection()
    {
        // A previously-saved 3-section layout opened today with a 1-section
        // window should merge the over-flowing tabs into the only available
        // section rather than dropping them.
        _documentsPanel.SectionCount.Returns(1);
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", WindowIndex: 0, SectionIndex: 2, TabOrder: 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync();

        await _documentsPanel.Received(1).OpenDocument(
            Arg.Any<ResourceKey>(),
            Arg.Is<OpenDocumentOptions>(options => options.Address!.SectionIndex == 0));
    }

    [Test]
    public async Task RestorePanelStateAsync_AttachesEditorStateJsonByResourceKey()
    {
        // Saved editor state is indexed by resource key, the canonical "project:..."
        // form ResourceKey.ToString emits. The restore forwards only the entry that
        // matches each opened tab.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, 0, 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        _propertyBag.GetPropertyAsync<Dictionary<string, string>>("DocumentEditorStates")
            .Returns(Task.FromResult<Dictionary<string, string>?>(new Dictionary<string, string>
            {
                [new ResourceKey("notes/readme.md").ToString()] = "{\"scroll\":0.5}",
                [new ResourceKey("other/file.md").ToString()] = "{\"scroll\":1.0}",
            }));

        await _store.RestorePanelStateAsync();

        await _documentsPanel.Received(1).OpenDocument(
            Arg.Any<ResourceKey>(),
            Arg.Is<OpenDocumentOptions>(options => options.EditorStateJson == "{\"scroll\":0.5}"));
    }

    [Test]
    public async Task RestorePanelStateAsync_RestoresActiveDocumentAfterOpens()
    {
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, 0, 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        _propertyBag.GetPropertyAsync<string>("ActiveDocument")
            .Returns(Task.FromResult<string?>("notes/readme.md"));

        await _store.RestorePanelStateAsync();

        _documentsPanel.Received().ActiveDocument = new ResourceKey("notes/readme.md");
    }

    [Test]
    public async Task RestorePanelStateAsync_NoStoredActiveDocument_StillSetsActiveDocument()
    {
        // Restored tabs with no persisted active document must still delegate to the panel so it
        // can enforce the one-active-document invariant.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, 0, 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("DocumentLayout")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        _propertyBag.GetPropertyAsync<string>("ActiveDocument")
            .Returns(Task.FromResult<string?>(null));

        await _store.RestorePanelStateAsync();

        _documentsPanel.Received().ActiveDocument = ResourceKey.Empty;
    }

    [Test]
    public async Task RestorePanelStateAsync_AppliesSectionRatiosWhenValid()
    {
        var ratios = new List<double> { 0.3, 0.7 };
        _propertyBag.GetPropertyAsync<List<double>>("SectionRatios")
            .Returns(Task.FromResult<List<double>?>(ratios));
        _resourceRegistry.NormalizeResourceKey(Arg.Any<ResourceKey>())
            .Returns(Result<ResourceKey>.Fail("not found"));

        await _store.RestorePanelStateAsync();

        _documentsPanel.Received().SectionCount = 2;
        _documentsPanel.Received(1).SetSectionRatios(ratios);
    }

    [Test]
    public async Task StoreActiveDocumentAsync_WritesPanelActiveDocumentString()
    {
        // The store reads the panel's active document directly (not the gated
        // IDocumentsService.ActiveDocument), so the real value is persisted even while the
        // workspace page is still loading.
        var resource = new ResourceKey("notes/readme.md");
        _documentsPanel.ActiveDocument.Returns(resource);

        await _store.StoreActiveDocumentAsync();

        // ResourceKey.ToString prefixes the default root, so the persisted
        // value is "project:notes/readme.md" rather than the bare path.
        await _propertyBag.Received(1).SetPropertyAsync("ActiveDocument", resource.ToString());
    }

    [Test]
    public async Task StoreSectionRatiosAsync_WritesRatiosList()
    {
        var ratios = new List<double> { 0.5, 0.5 };

        await _store.StoreSectionRatiosAsync(ratios);

        await _propertyBag.Received(1).SetPropertyAsync("SectionRatios", ratios);
    }

    [Test]
    public async Task StoreDocumentEditorStateAsync_WithStateUpdatesDictionary()
    {
        var targetResource = new ResourceKey("notes/readme.md");
        var otherResource = new ResourceKey("other/file.md");
        _propertyBag.GetPropertyAsync<Dictionary<string, string>>("DocumentEditorStates")
            .Returns(Task.FromResult<Dictionary<string, string>?>(new Dictionary<string, string>
            {
                [otherResource.ToString()] = "{\"scroll\":1.0}",
            }));

        await _store.StoreDocumentEditorStateAsync(targetResource, "{\"scroll\":0.5}");

        await _propertyBag.Received(1).SetPropertyAsync(
            "DocumentEditorStates",
            Arg.Is<Dictionary<string, string>>(d =>
                d[targetResource.ToString()] == "{\"scroll\":0.5}"
                && d[otherResource.ToString()] == "{\"scroll\":1.0}"));
    }

    [Test]
    public async Task StoreDocumentEditorStateAsync_WithNullRemovesEntry()
    {
        var targetResource = new ResourceKey("notes/readme.md");
        var otherResource = new ResourceKey("other/file.md");
        _propertyBag.GetPropertyAsync<Dictionary<string, string>>("DocumentEditorStates")
            .Returns(Task.FromResult<Dictionary<string, string>?>(new Dictionary<string, string>
            {
                [targetResource.ToString()] = "{\"scroll\":0.5}",
                [otherResource.ToString()] = "{\"scroll\":1.0}",
            }));

        await _store.StoreDocumentEditorStateAsync(targetResource, null);

        await _propertyBag.Received(1).SetPropertyAsync(
            "DocumentEditorStates",
            Arg.Is<Dictionary<string, string>>(d =>
                !d.ContainsKey(targetResource.ToString())
                && d[otherResource.ToString()] == "{\"scroll\":1.0}"));
    }
}
