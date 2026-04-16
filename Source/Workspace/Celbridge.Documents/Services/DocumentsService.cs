using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private const string DocumentLayoutKey = "DocumentLayout";
    private const string ActiveDocumentKey = "ActiveDocument";
    private const string SectionRatiosKey = "SectionRatios";
    private const string DocumentEditorStatesKey = "DocumentEditorStates";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentsService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;
    private readonly IFeatureFlags _featureFlags;

    /// <summary>
    /// Gets the documents panel from the workspace service.
    /// </summary>
    private IDocumentsPanel DocumentsPanel => _workspaceWrapper.WorkspaceService.DocumentsPanel;

    public ResourceKey ActiveDocument { get; private set; }

    /// <summary>
    /// Returns the currently open documents from the documents panel.
    /// Must be called on the UI thread because it reads TabView-backed state.
    /// MCP tools reach this via a Query command that runs on the command-queue worker,
    /// which executes on the UI thread.
    /// </summary>
    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => DocumentsPanel.GetOpenDocuments();

    /// <summary>
    /// Returns the number of visible document sections from the documents panel.
    /// Must be called on the UI thread because it reads TabView-backed state.
    /// MCP tools reach this via a Query command that runs on the command-queue worker,
    /// which executes on the UI thread.
    /// </summary>
    public int SectionCount => DocumentsPanel.SectionCount;

    private bool _isWorkspaceLoaded;

    private FileTypeHelper _fileTypeHelper;

    private readonly DocumentEditorRegistry _documentEditorRegistry = new();

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

        _messengerService.Register<PackagesInitializedMessage>(this, OnPackagesInitializedMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<DocumentLayoutChangedMessage>(this, OnDocumentLayoutChangedMessage);
        _messengerService.Register<ActiveDocumentChangedMessage>(this, OnActiveDocumentChangedMessage);
        _messengerService.Register<SectionRatiosChangedMessage>(this, OnSectionRatiosChangedMessage);
        _messengerService.Register<DocumentResourceChangedMessage>(this, OnDocumentResourceChangedMessage);

        // Register document editor factories from all loaded modules.
        // This must happen before FileTypeHelper initialization so factories can provide language mappings.
        RegisterModuleDocumentEditorFactories(moduleService);

        _fileTypeHelper = serviceProvider.GetRequiredService<FileTypeHelper>();
        _fileTypeHelper.SetDocumentEditorRegistry(_documentEditorRegistry);

        var loadResult = _fileTypeHelper.Initialize();
        if (loadResult.IsFailure)
        {
            throw new InvalidProgramException("Failed to initialize file type helper");
        }
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

                    var factory = new CustomDocumentViewFactory(_serviceProvider, customContribution, _featureFlags, localizationService);
                    var result = _documentEditorRegistry.RegisterFactory(factory);
                    if (result.IsFailure)
                    {
                        _logger.LogWarning(result,
                            $"Failed to register contribution editor factory for: {contribution.Package.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        $"An exception occurred while registering contribution editor for: {contribution.Package?.Name ?? contribution.Id}");
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
        // Once set, this will remain true for the lifetime of the service
        _isWorkspaceLoaded = true;
    }

    private void OnActiveDocumentChangedMessage(object recipient, ActiveDocumentChangedMessage message)
    {
        ActiveDocument = message.DocumentResource;

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreActiveDocument();
        }
    }

    private void OnDocumentLayoutChangedMessage(object recipient, DocumentLayoutChangedMessage message)
    {
        if (_isWorkspaceLoaded)
        {
            _ = StoreDocumentLayout();
        }
    }

    private void OnSectionRatiosChangedMessage(object recipient, SectionRatiosChangedMessage message)
    {
        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreSectionRatios(message.SectionRatios);
        }
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

        //
        // Load the content from the document file
        //

        var setFileResult = await documentView.SetFileResource(fileResource);
        if (setFileResult.IsFailure)
        {
            return Result.Fail($"Failed to set file resource for document view: '{fileResource}'")
                .WithErrors(setFileResult);
        }

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

    /// <summary>
    /// Returns the document view type for the specified file resource.
    /// </summary>
    public DocumentViewType GetDocumentViewType(ResourceKey fileResource)
    {
        var extension = Path.GetExtension(fileResource).ToLowerInvariant();

        // For unrecognized extensions (including empty), check if the file is text
        if (!_fileTypeHelper.IsRecognizedExtension(extension))
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
            if (resolveResult.IsFailure)
            {
                return DocumentViewType.UnsupportedFormat;
            }

            var result = _textBinarySniffer.IsTextFile(resolveResult.Value);
            if (result.IsFailure)
            {
                // Failed to determine if the file is text
                return DocumentViewType.UnsupportedFormat;
            }
            var isTextFile = result.Value;

            if (!isTextFile)
            {
                // We determined the file type, but it's not a text file.
                return DocumentViewType.UnsupportedFormat;
            }
        }

        return _fileTypeHelper.GetDocumentViewType(extension);
    }

    public bool IsDocumentSupported(ResourceKey fileResource)
    {
        // First check if any registered factory supports this extension
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        if (_documentEditorRegistry.IsExtensionSupported(extension))
        {
            return true;
        }

        // Fall back to built-in types
        var documentType = GetDocumentViewType(fileResource);
        return documentType != DocumentViewType.UnsupportedFormat;
    }

    public string GetDocumentLanguage(ResourceKey fileResource)
    {
        var extension = Path.GetExtension(fileResource).ToLowerInvariant();
        return _fileTypeHelper.GetTextEditorLanguage(extension);
    }

    public bool CanAccessFile(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath) ||
            !File.Exists(resourcePath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(resourcePath);
            using var stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Result<string> ResolveAndValidateFilePath(ResourceKey fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File path does not exist: '{filePath}'");
        }

        if (!CanAccessFile(filePath))
        {
            return Result.Fail($"File exists but cannot be opened: '{filePath}'");
        }

        return filePath;
    }

    public async Task<Result<OpenDocumentOutcome>> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null)
    {
        var resolveResult = ResolveAndValidateFilePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to open document for file resource '{fileResource}'")
                .WithErrors(resolveResult);
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

    /// <summary>
    /// DTO for serializing document addresses to workspace settings.
    /// </summary>
    private record StoredDocumentAddress(string Resource, int WindowIndex, int SectionIndex, int TabOrder, string DocumentEditorId = "");

    public async Task StoreDocumentLayout()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var storedAddresses = GetOpenDocuments()
            .Select(document => new StoredDocumentAddress(
                document.FileResource.ToString(),
                document.Address.WindowIndex,
                document.Address.SectionIndex,
                document.Address.TabOrder,
                document.EditorId.ToString()))
            .OrderBy(addr => addr.WindowIndex)
            .ThenBy(addr => addr.SectionIndex)
            .ThenBy(addr => addr.TabOrder)
            .ToList();

        await workspaceSettings.SetPropertyAsync(DocumentLayoutKey, storedAddresses);
    }


    public async Task StoreActiveDocument()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var fileResource = ActiveDocument.ToString();

        await workspaceSettings.SetPropertyAsync(ActiveDocumentKey, fileResource);
    }

    public async Task StoreSectionRatios(List<double> ratios)
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        await workspaceSettings.SetPropertyAsync(SectionRatiosKey, ratios);
    }

    public async Task StoreEditorStates()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        // Start with existing saved states so that editors that aren't ready yet
        // (e.g., WebView still loading) preserve their previously saved state.
        var editorStates = await workspaceSettings.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey)
            ?? new Dictionary<string, string>();

        // Track which documents are currently open so we can remove stale entries
        var openDocumentKeys = new HashSet<string>();

        foreach (var document in GetOpenDocuments())
        {
            var resourceKey = document.FileResource.ToString();
            openDocumentKeys.Add(resourceKey);

            var documentView = DocumentsPanel.GetDocumentView(document.FileResource);
            if (documentView is null)
            {
                continue;
            }

            if (!documentView.IsEditorStateReady)
            {
                // Editor still initializing — preserve existing saved state
                continue;
            }

            try
            {
                var state = await documentView.SaveEditorStateAsync();
                if (!string.IsNullOrEmpty(state))
                {
                    editorStates[resourceKey] = state;
                }
                else
                {
                    // Editor is ready but has no state — remove any stale entry
                    editorStates.Remove(resourceKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Could not save editor state for '{resourceKey}'");
            }
        }

        // Remove entries for documents that are no longer open
        var staleKeys = editorStates.Keys.Where(key => !openDocumentKeys.Contains(key)).ToList();
        foreach (var staleKey in staleKeys)
        {
            editorStates.Remove(staleKey);
        }

        await workspaceSettings.SetPropertyAsync(DocumentEditorStatesKey, editorStates);
    }

    public async Task ClearEditorState(ResourceKey fileResource)
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        try
        {
            var editorStates = await workspaceSettings.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);
            if (editorStates is not null && editorStates.Remove(fileResource.ToString()))
            {
                await workspaceSettings.SetPropertyAsync(DocumentEditorStatesKey, editorStates);
            }
        }
        catch (Exception ex)
        {
            // Clearing state is best-effort: losing a stale entry is harmless, but we still log
            // at debug level so unexpected failures are visible to developers.
            _logger.LogDebug(ex, $"Failed to clear editor state for '{fileResource}'");
        }
    }

    public async Task RestorePanelState()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Restore section layout (count is inferred from ratios list length)
        var sectionRatios = await workspaceSettings.GetPropertyAsync<List<double>>(SectionRatiosKey);
        if (sectionRatios != null && sectionRatios.Count >= 1 && sectionRatios.Count <= 3)
        {
            DocumentsPanel.SectionCount = sectionRatios.Count;
            DocumentsPanel.SetSectionRatios(sectionRatios);
        }

        // Try to load document addresses - if format is incompatible, just start fresh
        List<StoredDocumentAddress>? storedAddresses = null;
        try
        {
            storedAddresses = await workspaceSettings.GetPropertyAsync<List<StoredDocumentAddress>>(DocumentLayoutKey);
        }
        catch
        {
            // Old format or corrupted data - ignore and start fresh
            _logger.LogDebug("Could not load document addresses - starting fresh");
        }

        if (storedAddresses is null || storedAddresses.Count == 0)
        {
            // No documents to restore - open default readme
            await OpenDefaultReadme(resourceRegistry);
            return;
        }

        // Load saved editor states for restoration after documents are opened
        Dictionary<string, string>? editorStates = null;
        try
        {
            editorStates = await workspaceSettings.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);
        }
        catch
        {
            _logger.LogDebug("Could not load editor states - starting fresh");
        }

        int currentSectionCount = DocumentsPanel.SectionCount;

        foreach (var stored in storedAddresses)
        {
            if (!ResourceKey.TryCreate(stored.Resource, out var fileResource))
            {
                _logger.LogWarning($"Invalid resource key '{stored.Resource}' found in previously open documents");
                continue;
            }

            var getResourceResult = resourceRegistry.GetResource(fileResource);
            if (getResourceResult.IsFailure)
            {
                _logger.LogWarning(getResourceResult, $"Failed to open document because '{fileResource}' resource does not exist.");
                continue;
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
            if (resolveResult.IsFailure)
            {
                _logger.LogWarning(resolveResult, $"Failed to resolve path for resource: '{fileResource}'");
                continue;
            }
            var filePath = resolveResult.Value;

            if (!CanAccessFile(filePath))
            {
                _logger.LogWarning($"Cannot access file for resource: '{fileResource}'");
                continue;
            }

            // Handle mismatch: if saved section doesn't exist, merge into last section
            int targetSection = Math.Min(stored.SectionIndex, currentSectionCount - 1);
            var address = new DocumentAddress(stored.WindowIndex, targetSection, stored.TabOrder);

            // Use TryParse rather than the throwing constructor: a persisted editor id may reference
            // a package or contribution that has since been renamed or uninstalled, and an invalid
            // value should fall back to the default editor instead of aborting the restore.
            DocumentEditorId editorId;
            if (string.IsNullOrEmpty(stored.DocumentEditorId))
            {
                editorId = DocumentEditorId.Empty;
            }
            else if (!DocumentEditorId.TryParse(stored.DocumentEditorId, out editorId))
            {
                _logger.LogWarning($"Stored document editor id '{stored.DocumentEditorId}' is invalid and will be ignored for resource '{fileResource}'");
                editorId = DocumentEditorId.Empty;
            }

            string? editorStateJson = null;
            editorStates?.TryGetValue(fileResource.ToString(), out editorStateJson);

            var restoreOptions = new OpenDocumentOptions(
                Address: address,
                Activate: false,
                EditorId: editorId,
                EditorStateJson: editorStateJson);

            var openResult = await DocumentsPanel.OpenDocument(fileResource, restoreOptions);
            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open previously open document '{fileResource}'");
                await ClearEditorState(fileResource);
            }
        }

        // Restore selected document
        var selectedDocument = await workspaceSettings.GetPropertyAsync<string>(ActiveDocumentKey);
        if (string.IsNullOrEmpty(selectedDocument))
        {
            return;
        }

        if (!ResourceKey.TryCreate(selectedDocument, out var selectedDocumentKey))
        {
            _logger.LogWarning($"Invalid resource key '{selectedDocument}' found for previously selected document");
            return;
        }

        // Set the active document (which also selects it in its section)
        DocumentsPanel.ActiveDocument = selectedDocumentKey;

        // Ensure all sections with tabs have a visible selected tab, not just the active section
        DocumentsPanel.EnsureVisibleTabsSelected();
    }

    private async Task OpenDefaultReadme(IResourceRegistry resourceRegistry)
    {
        var readmeResource = new ResourceKey("readme.md");

        var normalizeResult = resourceRegistry.NormalizeResourceKey(readmeResource);
        if (normalizeResult.IsSuccess)
        {
            var normalizedResource = normalizeResult.Value;
            var resolveResult = resourceRegistry.ResolveResourcePath(normalizedResource);
            if (resolveResult.IsSuccess && CanAccessFile(resolveResult.Value))
            {
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = normalizedResource;
                    command.ForceReload = false;
                });
            }
        }
    }

    private async Task<Result<IDocumentView>> CreateDocumentViewInternalAsync(ResourceKey fileResource, DocumentEditorId documentEditorId = default)
    {
        // First, try to get a document view from the registry
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        // If a specific editor was requested, use it directly. Do not fall through to priority-based
        // resolution on failure: silently opening a different editor than the one the caller asked for
        // is confusing and hides real problems (e.g., "Open With..." handing a file to a factory that
        // cannot handle it).
        if (!documentEditorId.IsEmpty)
        {
            var getFactoryResult = _documentEditorRegistry.GetFactoryById(documentEditorId);
            if (getFactoryResult.IsFailure)
            {
                return Result.Fail($"No document editor is registered with id '{documentEditorId}'")
                    .WithErrors(getFactoryResult);
            }
            var requestedFactory = getFactoryResult.Value;

            if (!requestedFactory.CanHandleResource(fileResource, filePath))
            {
                return Result.Fail($"Document editor '{documentEditorId}' cannot handle file resource: '{fileResource}'");
            }

            var createResult = requestedFactory.CreateDocumentView(fileResource);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Document editor '{documentEditorId}' failed to create view for: '{fileResource}'")
                    .WithErrors(createResult);
            }

            return createResult;
        }

        // Check workspace preference for this extension
        if (documentEditorId.IsEmpty)
        {
            var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
            var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);
            var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
            var preferredEditorId = await workspaceSettings.GetPropertyAsync<string>(preferenceKey);

            if (!string.IsNullOrEmpty(preferredEditorId))
            {
                var getPreferredFactoryResult = _documentEditorRegistry.GetFactoryById(preferredEditorId);
                if (getPreferredFactoryResult.IsSuccess)
                {
                    var preferredFactory = getPreferredFactoryResult.Value;
                    if (preferredFactory.CanHandleResource(fileResource, filePath))
                    {
                        var createResult = preferredFactory.CreateDocumentView(fileResource);
                        if (createResult.IsSuccess)
                        {
                            return createResult;
                        }
                    }
                }
            }
        }

        // Fall back to priority-based factory resolution
        var factoryResult = _documentEditorRegistry.GetFactory(fileResource, filePath);
        if (factoryResult.IsSuccess)
        {
            var factory = factoryResult.Value;
            var createResult = factory.CreateDocumentView(fileResource);
            if (createResult.IsSuccess)
            {
                return createResult;
            }

            // Log the failure and fall through to fallback
            _logger.LogWarning(createResult, $"Factory failed to create document view for: '{fileResource}'");
        }

        // Fall back for text files when no factory is registered
        var viewType = GetDocumentViewType(fileResource);

        if (viewType == DocumentViewType.UnsupportedFormat)
        {
            return Result.Fail($"File resource is not a supported document format: '{fileResource}'");
        }

        // For text documents with unrecognized extensions, try to find a factory that can handle them
        // This allows CodeEditorFactory to handle arbitrary text files on Windows via TextBinarySniffer
        if (viewType == DocumentViewType.TextDocument)
        {
            // Check all factories to see if any can handle this text file
            foreach (var factory in _documentEditorRegistry.GetAllFactories().OrderBy(f => f.Priority))
            {
                if (factory.CanHandleResource(fileResource, filePath))
                {
                    var createResult = factory.CreateDocumentView(fileResource);
                    if (createResult.IsSuccess)
                    {
                        return createResult;
                    }
                }
            }

            // Ultimate fallback to TextBoxDocumentView.
            var textBoxView = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
            return textBoxView.OkResult<IDocumentView>();
        }

        return Result.Fail($"Failed to create document view for file: '{fileResource}'");
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

        Guard.IsTrue(File.Exists(newResourcePath));

        var oldExtension = Path.GetExtension(oldResource);
        var oldDocumentType = _fileTypeHelper.GetDocumentViewType(oldExtension);

        var newExtension = Path.GetExtension(newResource);
        var newDocumentType = _fileTypeHelper.GetDocumentViewType(newExtension);

        var changeDocumentResource = async Task () =>
        {
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

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                _messengerService.UnregisterAll(this);

                // Dispose the document editor registry to clean up factories
                _documentEditorRegistry.Dispose();
            }

            _disposed = true;
        }
    }

    ~DocumentsService()
    {
        Dispose(false);
    }
}
