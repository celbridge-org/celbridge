using Celbridge.Commands;
using Celbridge.Documents.Helpers;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentsService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;
    private readonly IFeatureFlags _featureFlags;
    private readonly FileTypeHelper _fileTypeHelper;
    private readonly DocumentEditorRegistry _documentEditorRegistry;
    private readonly FileTypeClassifier _fileTypeClassifier;
    private readonly DocumentEditorPreferenceStore _preferenceStore;
    private readonly DocumentViewFactory _viewFactory;
    private readonly DocumentLayoutStore _layoutStore;
    private readonly ReloadHintStore _reloadHintStore = new(TimeSpan.FromSeconds(2));

    private bool _disposed;

    private IDocumentsPanel DocumentsPanel => _workspaceWrapper.WorkspaceService.DocumentsPanel;

    /// <summary>
    /// The currently active document, sourced from the documents panel. Returns
    /// Empty before the workspace page is loaded (the panel does not exist yet).
    /// </summary>
    public ResourceKey ActiveDocument =>
        _workspaceWrapper.IsWorkspacePageLoaded
            ? DocumentsPanel.ActiveDocument
            : ResourceKey.Empty;

    /// <summary>
    /// Returns the currently open documents from the documents panel.
    /// Reads TabView-backed state, so callers must be on the UI thread. MCP tools satisfy this
    /// by going through a query command on the command-queue worker, which executes on the UI thread.
    /// </summary>
    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => DocumentsPanel.GetOpenDocuments();

    /// <summary>
    /// Returns the number of visible document sections. Reads an int, which is atomic in .NET,
    /// so this property is safe to call from any thread.
    /// </summary>
    public int SectionCount => DocumentsPanel.SectionCount;

    public IDocumentEditorRegistry DocumentEditorRegistry => _documentEditorRegistry;

    public DocumentsService(
        IServiceProvider serviceProvider,
        ILogger<DocumentsService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IModuleService moduleService,
        IWorkspaceWrapper workspaceWrapper,
        ITextBinarySniffer textBinarySniffer,
        IFeatureFlags featureFlags)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _logger = logger;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _textBinarySniffer = textBinarySniffer;
        _featureFlags = featureFlags;
        _documentEditorRegistry = new DocumentEditorRegistry(_textBinarySniffer);

        _messengerService.Register<PackagesInitializedMessage>(this, OnPackagesInitializedMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<DocumentResourceChangedMessage>(this, OnDocumentResourceChangedMessage);

        // The layout / active / section subscriptions are deferred to
        // OnWorkspaceLoadedMessage so the messages fired by RestorePanelState
        // (which runs before workspace-loaded is published) do not trigger
        // settings writes for what we just read out of settings.

        // Register document editor factories from all loaded modules.
        // This must happen before FileTypeHelper initialization so factories can provide language mappings.
        RegisterModuleDocumentEditorFactories(moduleService);

        _fileTypeHelper = new FileTypeHelper();
        _fileTypeHelper.SetDocumentEditorRegistry(_documentEditorRegistry);

        var loadResult = _fileTypeHelper.Initialize();
        if (loadResult.IsFailure)
        {
            throw new InvalidProgramException("Failed to initialize file type helper");
        }

        _fileTypeClassifier = new FileTypeClassifier(
            _fileTypeHelper,
            _textBinarySniffer,
            _workspaceWrapper,
            _documentEditorRegistry);

        _preferenceStore = new DocumentEditorPreferenceStore(
            _workspaceWrapper,
            serviceProvider.GetRequiredService<ILogger<DocumentEditorPreferenceStore>>());

        // Built after the registry is fully populated so the factory sees
        // every editor it might choose.
        _viewFactory = new DocumentViewFactory(
            _documentEditorRegistry,
            _workspaceWrapper,
            _preferenceStore,
            _fileTypeClassifier,
            _serviceProvider,
            serviceProvider.GetRequiredService<ILogger<DocumentViewFactory>>());

        _layoutStore = new DocumentLayoutStore(
            _workspaceWrapper,
            _commandService,
            serviceProvider.GetRequiredService<ILogger<DocumentLayoutStore>>());
    }

    private void RegisterModuleDocumentEditorFactories(IModuleService moduleService)
    {
        foreach (var module in moduleService.LoadedModules)
        {
            try
            {
                var factories = module.CreateDocumentEditorFactories(_serviceProvider);
                foreach (var factory in factories)
                {
                    var result = _documentEditorRegistry.RegisterFactory(factory);
                    if (result.IsFailure)
                    {
                        _logger.LogWarning(result, $"Failed to register document editor factory");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"An exception occurred while registering editor factories from module: {module.GetType().Name}");
            }
        }
    }

    private void OnPackagesInitializedMessage(object recipient, PackagesInitializedMessage message)
    {
        try
        {
            var workspaceService = _workspaceWrapper.WorkspaceService;
            var contributions = workspaceService.PackageService.GetAllDocumentEditors();
            var localizationService = _serviceProvider.GetRequiredService<IPackageLocalizationService>();

            foreach (var contribution in contributions)
            {
                try
                {
                    if (contribution is not CustomDocumentEditorContribution customContribution)
                    {
                        continue;
                    }

                    // Register a document editor factory for each custom contribution, including utilities: a
                    // utility's factory is what lets it be docked into a document tab. Its IsUtility flag keeps
                    // it out of the New File dialog and the Reopen-with menu, and its utils: backing resource
                    // keeps it out of the project-scoped resource tree and search.
                    var factory = new CustomDocumentViewFactory(_serviceProvider, customContribution, _featureFlags, localizationService);
                    var result = _documentEditorRegistry.RegisterFactory(factory);
                    if (result.IsFailure)
                    {
                        _logger.LogWarning(result,
                            $"Failed to register custom editor factory for: {contribution?.Package?.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        $"An exception occurred while registering custom editor for: {contribution.Package?.Name ?? contribution.Id}");
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An exception occurred while initializing package document editors");
        }
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        _messengerService.Register<DocumentLayoutChangedMessage>(this, OnDocumentLayoutChangedMessage);
        _messengerService.Register<ActiveDocumentChangedMessage>(this, OnActiveDocumentChangedMessage);
        _messengerService.Register<SectionRatiosChangedMessage>(this, OnSectionRatiosChangedMessage);
    }

    private void OnActiveDocumentChangedMessage(object recipient, ActiveDocumentChangedMessage message)
    {
        _ = StoreActiveDocument();
    }

    private void OnDocumentLayoutChangedMessage(object recipient, DocumentLayoutChangedMessage message)
    {
        _ = StoreDocumentLayout();
    }

    private void OnSectionRatiosChangedMessage(object recipient, SectionRatiosChangedMessage message)
    {
        _ = _layoutStore.StoreSectionRatiosAsync(message.SectionRatios);
    }

    public async Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource, DocumentEditorId documentEditorId = default)
    {
        //
        // Create the appropriate document view control for this document type
        //

        var createResult = await CreateDocumentViewInternalAsync(fileResource, documentEditorId);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        // Factories must set view.EditorId before returning; catch a missed stamp here.
        if (documentView.EditorId.IsEmpty)
        {
            return Result.Fail(
                $"Document view for '{fileResource}' was returned with an empty EditorId. " +
                "The factory that produced it must set view.EditorId before returning.");
        }

        //
        // Load the content from the document file
        //

        var setFileResult = await documentView.SetFileResource(fileResource);
        if (setFileResult.IsFailure)
        {
            return Result.Fail($"Failed to set file resource for document view: '{fileResource}'")
                .WithErrors(setFileResult);
        }

        // Applied after SetFileResource and before LoadContent so the editor
        // enters read-only mode before its first setValue. For CustomDocumentView
        // the state ships through the document/initialize handshake; for editors
        // that drive their surface directly, OnWritableStateChanged fires here.
        var operationService = _workspaceWrapper.WorkspaceService.ResourceService.Operations;
        var writableState = await operationService.GetWritableStateAsync(fileResource);
        documentView.SetWritableState(writableState);

        var loadResult = await documentView.LoadContent();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for document view: '{fileResource}'")
                .WithErrors(loadResult);
        }

        // IDocumentView is an interface, so the implicit T -> Result<T> conversion doesn't apply;
        // use the OkResult extension method to wrap the value.
        return documentView.OkResult();
    }

    public DocumentViewType GetDocumentViewType(ResourceKey fileResource) =>
        _fileTypeClassifier.GetDocumentViewType(fileResource);

    public IFindableDocument? GetActiveFindableDocument()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return null;
        }

        var activeDocumentView = DocumentsPanel.GetDocumentView(ActiveDocument);

        return activeDocumentView as IFindableDocument;
    }

    public bool IsDocumentSupported(ResourceKey fileResource) =>
        _fileTypeClassifier.IsDocumentSupported(fileResource);

    public string GetDocumentLanguage(ResourceKey fileResource)
    {
        var extension = Path.GetExtension(fileResource).ToLowerInvariant();
        return _fileTypeHelper.GetTextEditorLanguage(extension);
    }

    public Task<DocumentEditorId> GetPreferredEditorAsync(ResourceKey fileResource) =>
        _preferenceStore.GetPreferredEditorAsync(fileResource);

    public async Task<Result> SetPreferredEditorAsync(ResourceKey fileResource, DocumentEditorId editorId, bool useAsExtensionDefault)
    {
        if (useAsExtensionDefault)
        {
            var extension = Path.GetExtension(fileResource.Path).ToLowerInvariant();
            await _preferenceStore.SetExtensionPreferenceAsync(extension, editorId);
        }

        // ToString() forces a string value; passing the DocumentEditorId struct
        // directly boxes it and SidecarService rejects non-scalar values.
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [SidecarFieldNames.Editor] = editorId.ToString(),
        };

        return await _commandService.ExecuteAsync<ISetFieldsCommand>(command =>
        {
            command.Resource = fileResource;
            command.Fields = fields;
        });
    }

    public async Task<Result<OpenDocumentOutcome>> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null)
    {
        // A utility is only ever presented by docking, never opened as an ordinary document. This is the
        // chokepoint every open routes through, so refusing utils: here prevents a second, uncontrolled instance.
        if (fileResource.Root == ProjectConstants.UtilsFolder)
        {
            return Result.Fail($"Cannot open utility resource '{fileResource}' as a document. Utilities are presented through the Utility Panel and docked into a tab, never opened directly.");
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var infoResult = await resourceFileSystem.GetInfoAsync(fileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"Failed to open document for file resource '{fileResource}': file does not exist")
                .WithErrors(infoResult);
        }

        var openResult = await DocumentsPanel.OpenDocument(fileResource, options);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open document for file resource '{fileResource}'")
                .WithErrors(openResult);
        }
        var outcome = openResult.Value;

        if (outcome == OpenDocumentOutcome.Cancelled)
        {
            _logger.LogTrace($"Open document was cancelled for file resource '{fileResource}'");
        }
        else
        {
            _logger.LogTrace($"Opened document for file resource '{fileResource}'");
        }

        return outcome;
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose)
    {
        var closeResult = await DocumentsPanel.CloseDocument(fileResource, forceClose);
        if (closeResult.IsFailure)
        {
            return Result.Fail($"Failed to close document for file resource '{fileResource}'")
                .WithErrors(closeResult);
        }

        _logger.LogTrace($"Closed document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public Result ActivateDocument(ResourceKey fileResource)
    {
        var activateResult = DocumentsPanel.ActivateDocument(fileResource);
        if (activateResult.IsFailure)
        {
            return Result.Fail($"Failed to activate opened document for file resource '{fileResource}'")
                .WithErrors(activateResult);
        }

        _logger.LogTrace($"Activated document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public async Task<Result> SaveModifiedDocuments(double deltaTime)
    {
        var saveResult = await DocumentsPanel.SaveModifiedDocuments(deltaTime);
        if (saveResult.IsFailure)
        {
            return Result.Fail("Failed to save modified documents")
                .WithErrors(saveResult);
        }

        return Result.Ok();
    }

    public Task StoreDocumentLayout() => _layoutStore.StoreDocumentLayoutAsync();

    public Task StoreActiveDocument() => _layoutStore.StoreActiveDocumentAsync();

    public Task StoreDocumentEditorStates() => _layoutStore.StoreDocumentEditorStatesAsync();

    public Task StoreDocumentEditorState(ResourceKey fileResource, string? state) =>
        _layoutStore.StoreDocumentEditorStateAsync(fileResource, state);

    public Task RestorePanelState() => _layoutStore.RestorePanelStateAsync();

    private Task<Result<IDocumentView>> CreateDocumentViewInternalAsync(ResourceKey fileResource, DocumentEditorId documentEditorId = default)
    {
        return _viewFactory.CreateAsync(fileResource, documentEditorId);
    }

    private void OnDocumentResourceChangedMessage(object recipient, DocumentResourceChangedMessage message)
    {
        var oldResource = message.OldResource.ToString();
        var newResource = message.NewResource.ToString();

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(message.NewResource);
        if (resolveResult.IsFailure)
        {
            _logger.LogError(resolveResult, $"Failed to resolve path for renamed resource: '{message.NewResource}'");
            return;
        }
        var newResourcePath = resolveResult.Value;

        var oldDocumentType = _fileTypeHelper.GetDocumentViewType(oldResource);
        var newDocumentType = _fileTypeHelper.GetDocumentViewType(newResource);

        var changeDocumentResource = async Task () =>
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var infoResult = await resourceFileSystem.GetInfoAsync(message.NewResource);
            Guard.IsTrue(infoResult.IsSuccess && infoResult.Value.Kind == StorageItemKind.File);

            var changeResult = await DocumentsPanel.ChangeDocumentResource(oldResource, oldDocumentType, newResource, newResourcePath, newDocumentType);
            if (changeResult.IsFailure)
            {
                // Log the error and close the document to get back to a consistent state
                _logger.LogError(changeResult, $"Failed to change document resource from '{oldResource}' to '{newResource}'");
                await CloseDocument(oldResource, true);
            }
        };

        _ = changeDocumentResource();
    }

    public void RegisterReloadHint(ResourceKey fileResource, ReloadHint hint)
    {
        _reloadHintStore.Register(fileResource, hint);
    }

    public ReloadHint ConsumeReloadHint(ResourceKey fileResource)
    {
        return _reloadHintStore.Consume(fileResource);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _messengerService.UnregisterAll(this);
        _documentEditorRegistry.Dispose();
    }
}
