using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentLayoutStore: restore-parsing edge cases (corrupted layout,
/// invalid resource keys, section clamps), the documents a project opens on load,
/// and the basic settings-writing shape of the Store* methods.
/// </summary>
[TestFixture]
public class DocumentLayoutStoreTests
{
    private static readonly IReadOnlyList<DocumentShortcut> NoDocumentShortcuts = Array.Empty<DocumentShortcut>();

    private IWorkspacePropertyBag _propertyBag = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IDocumentsPanel _documentsPanel = null!;
    private IUtilityService _utilityService = null!;
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

        // Default registry behaviour: every key resolves to the accessible temp
        // file and exists in the registry. Individual tests override these
        // when they want to exercise the negative branches.
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Ok(_accessibleFilePath));
        _resourceRegistry.GetResource(Arg.Any<ResourceKey>())
            .Returns(Result<IResource>.Ok(Substitute.For<IResource>()));

        _documentsPanel.OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>())
            .Returns(Task.FromResult(Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Opened)));

        // No document is open until a test says one is.
        _documentsPanel.GetOpenDocuments().Returns(new List<OpenDocumentInfo>());

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
        _utilityService.RestoreDockedUtilityAsync(Arg.Any<ResourceKey>(), Arg.Any<DocumentAddress>())
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
    public async Task RestorePanelStateAsync_MalformedLayoutJson_DoesNotThrow()
    {
        // Old format / corrupted settings: GetPropertyAsync throws inside the
        // store, which catches and treats the layout as empty.
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns<Task<List<DocumentLayoutStore.StoredDocumentAddress>?>>(_ => throw new InvalidOperationException("bad json"));

        Func<Task> act = async () => await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task RestorePanelStateAsync_RestoresStoredAddressesViaPanelOpen()
    {
        // One stored doc: the store should call panel.OpenDocument with an
        // empty editor id (sidecar wins at restore) and the saved address.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", WindowIndex: 0, Section: "main_left", TabOrder: 2),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await _documentsPanel.Received(1).OpenDocument(
            new ResourceKey("notes/readme.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.EditorId == EditorId.Empty
                && options.Activate == false
                && options.Address!.Section == DocumentSection.MainLeft
                && options.Address.TabOrder == 2));
    }

    [Test]
    public async Task RestorePanelStateAsync_InvalidResourceKey_IsSkipped()
    {
        // A stored address whose Resource string isn't a valid ResourceKey
        // must not abort the rest of the restore.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("///invalid///", 0, "main_left", 0),
            new("notes/readme.md", 0, "main_left", 1),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

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
            new("notes/readme.md", 0, "main_left", 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await _documentsPanel.DidNotReceive().OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_UtilityResource_DocksViaDocumentsService()
    {
        // A utils: resource is a utility, a permanent Utility Panel item instantiated eagerly at load. A
        // stored utils: entry means it was docked last session, so the restore drives the dock mechanism to
        // reparent the already-live utility into its saved position rather than opening a second document.
        var utilityResource = new ResourceKey("utils:settings._notepad");
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new(utilityResource.ToString(), WindowIndex: 0, Section: "main_left", TabOrder: 3),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await _utilityService.Received(1).RestoreDockedUtilityAsync(
            utilityResource,
            Arg.Is<DocumentAddress>(address => address.Section == DocumentSection.MainLeft && address.TabOrder == 3));
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
            new("notes/readme.md", 0, "main_left", 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await _documentsPanel.DidNotReceive().OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_SecondarySectionOfUnsplitArea_SplitsTheArea()
    {
        // A tab saved in an area's secondary section splits that area on restore, so it lands where it
        // was left rather than folding into the primary section.
        _documentsPanel.IsAreaSplit(DocumentArea.Main).Returns(false);
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", WindowIndex: 0, Section: "main_right", TabOrder: 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        _documentsPanel.Received(1).SetAreaSplit(DocumentArea.Main, true);

        await _documentsPanel.Received(1).OpenDocument(
            Arg.Any<ResourceKey>(),
            Arg.Is<OpenDocumentOptions>(options => options.Address!.Section == DocumentSection.MainRight));
    }

    [Test]
    public async Task RestorePanelStateAsync_ReconcilesEveryAreaAfterRestoring()
    {
        // A document whose file has gone leaves the section it was restoring into empty, so the restore
        // folds away any split that ended up with nothing in it.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", WindowIndex: 0, Section: "main_left", TabOrder: 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            _documentsPanel.Received(1).ReconcileAreaSplit(area);
        }
    }

    [Test]
    public async Task RestorePanelStateAsync_AttachesEditorStateByResourceKey()
    {
        // Saved editor state is indexed by resource key, the canonical "project:..."
        // form ResourceKey.ToString emits. The restore forwards only the entry that
        // matches each opened tab, naming the editor that saved it.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, "main_left", 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        StubStoredEditorStates(new Dictionary<string, DocumentLayoutStore.StoredEditorState>
        {
            [new ResourceKey("notes/readme.md").ToString()] = new("celbridge.markdown", "{\"scroll\":0.5}"),
            [new ResourceKey("other/file.md").ToString()] = new("celbridge.markdown", "{\"scroll\":1.0}"),
        });

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await _documentsPanel.Received(1).OpenDocument(
            Arg.Any<ResourceKey>(),
            Arg.Is<OpenDocumentOptions>(options =>
                options.EditorState != null &&
                options.EditorState.Json == "{\"scroll\":0.5}" &&
                options.EditorState.EditorId == new EditorId("celbridge.markdown")));
    }

    [Test]
    public async Task RestorePanelStateAsync_DropsStateThatNamesNoEditor()
    {
        // A state naming no editor cannot be matched against the editor that opens the file.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, "main_left", 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        StubStoredEditorStates(new Dictionary<string, DocumentLayoutStore.StoredEditorState>
        {
            [new ResourceKey("notes/readme.md").ToString()] = new(string.Empty, "{\"scroll\":0.5}"),
        });

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        await _documentsPanel.Received(1).OpenDocument(
            Arg.Any<ResourceKey>(),
            Arg.Is<OpenDocumentOptions>(options => options.EditorState == null));
    }

    [Test]
    public async Task RestorePanelStateAsync_RestoresActiveDocumentAfterOpens()
    {
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, "main_left", 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        _propertyBag.GetPropertyAsync<string>("ActiveDocument")
            .Returns(Task.FromResult<string?>("notes/readme.md"));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        _documentsPanel.Received().ActiveDocument = new ResourceKey("notes/readme.md");
    }

    [Test]
    public async Task RestorePanelStateAsync_NoStoredActiveDocument_StillSetsActiveDocument()
    {
        // Restored tabs with no persisted active document must still delegate to the panel so it
        // can enforce the one-active-document invariant.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", 0, "main_left", 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));
        _propertyBag.GetPropertyAsync<string>("ActiveDocument")
            .Returns(Task.FromResult<string?>(null));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        _documentsPanel.Received().ActiveDocument = ResourceKey.Empty;
    }

    [Test]
    public async Task RestorePanelStateAsync_AppliesStoredSplitRatio()
    {
        var areaSplitRatios = new Dictionary<string, DocumentLayoutStore.StoredAreaSplitRatio>
        {
            ["main"] = new DocumentLayoutStore.StoredAreaSplitRatio(SplitRatio: 0.3),
        };
        _propertyBag.GetPropertyAsync<Dictionary<string, DocumentLayoutStore.StoredAreaSplitRatio>>("AreaSplitRatios")
            .Returns(Task.FromResult<Dictionary<string, DocumentLayoutStore.StoredAreaSplitRatio>?>(areaSplitRatios));

        await _store.RestorePanelStateAsync(NoDocumentShortcuts);

        _documentsPanel.Received(1).SetAreaSplitRatio(DocumentArea.Main, 0.3);

        // Split state is not restored from settings: it follows the documents that restore into each
        // section, so an area only splits when a document lands in its secondary one.
        _documentsPanel.DidNotReceive().SetAreaSplit(DocumentArea.Main, true);
    }

    [Test]
    public async Task RestorePanelStateAsync_OpenOnLoadShortcut_OpensInItsAreaWithoutActivating()
    {
        // Nothing was open last session, so the document joins the end of the area its shortcut declares. A
        // shortcut that does not open on load stays closed.
        var documentShortcuts = new List<DocumentShortcut>
        {
            new() { Resource = "notes/todo.md", Area = WorkspaceArea.Bottom, OpenOnLoad = true },
            new() { Resource = "notes/later.md" },
        };

        await _store.RestorePanelStateAsync(documentShortcuts);

        await _documentsPanel.Received(1).OpenDocument(
            new ResourceKey("notes/todo.md"),
            Arg.Is<OpenDocumentOptions>(options =>
                options.Activate == false
                && options.Address!.Section == DocumentSection.BottomLeft
                && options.Address.TabOrder == DocumentAddress.AppendTabOrder));
        await _documentsPanel.Received(1).OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
    }

    [Test]
    public async Task RestorePanelStateAsync_OpenOnLoadShortcutRestoredFromLastSession_KeepsItsPlace()
    {
        // Opening the document again for its shortcut would move the restored tab into the declared area.
        var stored = new List<DocumentLayoutStore.StoredDocumentAddress>
        {
            new("notes/readme.md", WindowIndex: 0, Section: "main_right", TabOrder: 0),
        };
        _propertyBag.GetPropertyAsync<List<DocumentLayoutStore.StoredDocumentAddress>>("OpenDocumentAddresses")
            .Returns(Task.FromResult<List<DocumentLayoutStore.StoredDocumentAddress>?>(stored));

        var restoredResource = new ResourceKey("notes/readme.md");
        var restoredAddress = new DocumentAddress(WindowIndex: 0, Section: DocumentSection.MainRight, TabOrder: 0);
        _documentsPanel.GetOpenDocuments().Returns(new List<OpenDocumentInfo>
        {
            new(restoredResource, restoredAddress, EditorId.Empty),
        });

        var documentShortcuts = new List<DocumentShortcut>
        {
            new() { Resource = "notes/readme.md", OpenOnLoad = true },
        };

        await _store.RestorePanelStateAsync(documentShortcuts);

        await _documentsPanel.Received(1).OpenDocument(Arg.Any<ResourceKey>(), Arg.Any<OpenDocumentOptions?>());
        await _documentsPanel.Received(1).OpenDocument(
            restoredResource,
            Arg.Is<OpenDocumentOptions>(options => options.Address!.Section == DocumentSection.MainRight));
    }

    [Test]
    public async Task RestorePanelStateAsync_NoStoredLayout_StillSetsActiveDocument()
    {
        // A document opened for its shortcut may be the only one open, so the panel still chooses an active
        // document when the last session left nothing open.
        var documentShortcuts = new List<DocumentShortcut>
        {
            new() { Resource = "notes/todo.md", OpenOnLoad = true },
        };

        await _store.RestorePanelStateAsync(documentShortcuts);

        _documentsPanel.Received().ActiveDocument = ResourceKey.Empty;
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
    public async Task StoreAreaSplitRatiosAsync_WritesSplitRatioPerArea()
    {
        _documentsPanel.GetAreaSplitRatio(DocumentArea.Main).Returns(0.4);
        _documentsPanel.GetAreaSplitRatio(DocumentArea.Bottom).Returns(0.5);

        await _store.StoreAreaSplitRatiosAsync();

        await _propertyBag.Received(1).SetPropertyAsync(
            "AreaSplitRatios",
            Arg.Is<Dictionary<string, DocumentLayoutStore.StoredAreaSplitRatio>>(layout =>
                layout["main"].SplitRatio == 0.4
                && layout["bottom"].SplitRatio == 0.5));
    }

    [Test]
    public async Task StoreDocumentEditorStateAsync_WithStateUpdatesDictionary()
    {
        var targetResource = new ResourceKey("notes/readme.md");
        var otherResource = new ResourceKey("other/file.md");
        StubStoredEditorStates(new Dictionary<string, DocumentLayoutStore.StoredEditorState>
        {
            [otherResource.ToString()] = new("celbridge.markdown", "{\"scroll\":1.0}"),
        });

        var state = new DocumentEditorState(new EditorId("celbridge.markdown"), "{\"scroll\":0.5}");
        await _store.StoreDocumentEditorStateAsync(targetResource, state);

        await _propertyBag.Received(1).SetPropertyAsync(
            "DocumentEditorStates",
            Arg.Is<Dictionary<string, DocumentLayoutStore.StoredEditorState>>(d =>
                d[targetResource.ToString()].State == "{\"scroll\":0.5}"
                && d[targetResource.ToString()].EditorId == "celbridge.markdown"
                && d[otherResource.ToString()].State == "{\"scroll\":1.0}"));
    }

    [Test]
    public async Task StoreDocumentEditorStateAsync_WithNullRemovesEntry()
    {
        var targetResource = new ResourceKey("notes/readme.md");
        var otherResource = new ResourceKey("other/file.md");
        StubStoredEditorStates(new Dictionary<string, DocumentLayoutStore.StoredEditorState>
        {
            [targetResource.ToString()] = new("celbridge.markdown", "{\"scroll\":0.5}"),
            [otherResource.ToString()] = new("celbridge.markdown", "{\"scroll\":1.0}"),
        });

        await _store.StoreDocumentEditorStateAsync(targetResource, null);

        await _propertyBag.Received(1).SetPropertyAsync(
            "DocumentEditorStates",
            Arg.Is<Dictionary<string, DocumentLayoutStore.StoredEditorState>>(d =>
                !d.ContainsKey(targetResource.ToString())
                && d[otherResource.ToString()].State == "{\"scroll\":1.0}"));
    }

    private void StubStoredEditorStates(Dictionary<string, DocumentLayoutStore.StoredEditorState> editorStates)
    {
        _propertyBag.GetPropertyAsync<Dictionary<string, DocumentLayoutStore.StoredEditorState>>("DocumentEditorStates")
            .Returns(Task.FromResult<Dictionary<string, DocumentLayoutStore.StoredEditorState>?>(editorStates));
    }
}
