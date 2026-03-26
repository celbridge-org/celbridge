using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Extensions;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private const string DocumentLayoutKey = "DocumentLayout";
    private const string ActiveDocumentKey = "ActiveDocument";
    private const string SectionRatiosKey = "SectionRatios";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentsService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFeatureFlags _featureFlags;

    /// <summary>
    /// Gets the documents panel from the workspace service.
    /// </summary>
    private IDocumentsPanel DocumentsPanel => _workspaceWrapper.WorkspaceService.DocumentsPanel;

    public ResourceKey ActiveDocument { get; private set; }

    /// <summary>
    /// Gets all open documents with their addresses (UI positions).
    /// </summary>
    public Dictionary<ResourceKey, DocumentAddress> OpenDocumentAddresses { get; } = new();

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
        IFeatureFlags featureFlags)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _logger = logger;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _featureFlags = featureFlags;

        _messengerService.Register<ExtensionsInitializedMessage>(this, OnExtensionsInitializedMessage);
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
    }

    private void OnExtensionsInitializedMessage(object recipient, ExtensionsInitializedMessage message)
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var contributions = workspaceService.ExtensionService.GetAllDocumentEditors();

        foreach (var contribution in contributions)
        {
            if (contribution is not CustomDocumentContribution customContribution)
            {
                continue;
            }

            var factory = new CustomDocumentViewFactory(_serviceProvider, customContribution, _featureFlags);
            var result = _documentEditorRegistry.RegisterFactory(factory);
            if (result.IsFailure)
            {
                _logger.LogWarning(result,
                    $"Failed to register extension editor factory for: {contribution.Extension.Name}");
            }
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
        // Query the panel for current document addresses
        var addresses = DocumentsPanel.GetDocumentAddresses();

        OpenDocumentAddresses.Clear();
        foreach (var kvp in addresses)
        {
            OpenDocumentAddresses[kvp.Key] = kvp.Value;
        }

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
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

    public async Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource)
    {
        //
        // Create the appropriate document view control for this document type
        //

        var createResult = CreateDocumentViewInternal(fileResource);
        if (createResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        //
        // Load the content from the document file
        //

        var setFileResult = await documentView.SetFileResource(fileResource);
        if (setFileResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to set file resource for document view: '{fileResource}'")
                .WithErrors(setFileResult);
        }

        var loadResult = await documentView.LoadContent();
        if (loadResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to load content for document view: '{fileResource}'")
                .WithErrors(loadResult);
        }

        return Result<IDocumentView>.Ok(documentView);
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
            var filePath = resourceRegistry.GetResourcePath(fileResource);

            var result = TextBinarySniffer.IsTextFile(filePath);
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

    public async Task<Result> OpenDocument(ResourceKey fileResource, bool forceReload = false, string location = "", bool activate = true)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var filePath = resourceRegistry.GetResourcePath(fileResource);
        if (string.IsNullOrEmpty(filePath) ||
            !File.Exists(filePath))
        {
            return Result.Fail($"File path does not exist: '{filePath}'");
        }

        if (!CanAccessFile(filePath))
        {
            return Result.Fail($"File exists but cannot be opened: '{filePath}'");
        }

        var openResult = await DocumentsPanel.OpenDocument(fileResource, filePath, forceReload, location, activate);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open document for file resource '{fileResource}'")
                .WithErrors(openResult);
        }

        _logger.LogTrace($"Opened document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public async Task<Result> OpenDocumentAtSection(ResourceKey fileResource, int sectionIndex, bool forceReload = false, string location = "", bool activate = true)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var filePath = resourceRegistry.GetResourcePath(fileResource);
        if (string.IsNullOrEmpty(filePath) ||
            !File.Exists(filePath))
        {
            return Result.Fail($"File path does not exist: '{filePath}'");
        }

        if (!CanAccessFile(filePath))
        {
            return Result.Fail($"File exists but cannot be opened: '{filePath}'");
        }

        var address = new DocumentAddress(WindowIndex: 0, SectionIndex: sectionIndex, TabOrder: 0);
        var openResult = await DocumentsPanel.OpenDocumentAtAddress(fileResource, filePath, address, activate);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open document for file resource '{fileResource}' at section {sectionIndex}")
                .WithErrors(openResult);
        }

        // Navigate to location if specified
        if (!string.IsNullOrEmpty(location))
        {
            await DocumentsPanel.NavigateToLocation(fileResource, location);
        }

        _logger.LogTrace($"Opened document for file resource '{fileResource}' at section {sectionIndex}");

        return Result.Ok();
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
    private record StoredDocumentAddress(string Resource, int WindowIndex, int SectionIndex, int TabOrder);

    public async Task StoreDocumentLayout()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        // Store documents with their addresses
        var storedAddresses = OpenDocumentAddresses
            .Select(kvp => new StoredDocumentAddress(
                kvp.Key.ToString(),
                kvp.Value.WindowIndex,
                kvp.Value.SectionIndex,
                kvp.Value.TabOrder))
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

            var filePath = resourceRegistry.GetResourcePath(fileResource);
            if (!CanAccessFile(filePath))
            {
                _logger.LogWarning($"Cannot access file for resource: '{fileResource}'");
                continue;
            }

            // Handle mismatch: if saved section doesn't exist, merge into last section
            int targetSection = Math.Min(stored.SectionIndex, currentSectionCount - 1);
            var address = new DocumentAddress(stored.WindowIndex, targetSection, stored.TabOrder);

            var openResult = await DocumentsPanel.OpenDocumentAtAddress(fileResource, filePath, address);
            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open previously open document '{fileResource}'");
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
    }

    private async Task OpenDefaultReadme(IResourceRegistry resourceRegistry)
    {
        var readmeResource = new ResourceKey("readme.md");

        var normalizeResult = resourceRegistry.NormalizeResourceKey(readmeResource);
        if (normalizeResult.IsSuccess)
        {
            var normalizedResource = normalizeResult.Value;
            var readmePath = resourceRegistry.GetResourcePath(normalizedResource);
            if (CanAccessFile(readmePath))
            {
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = normalizedResource;
                    command.ForceReload = false;
                });
            }
        }
    }

    private Result<IDocumentView> CreateDocumentViewInternal(ResourceKey fileResource)
    {
        // First, try to get a document view from the registry
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var filePath = resourceRegistry.GetResourcePath(fileResource);

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
            return Result<IDocumentView>.Fail($"File resource is not a supported document format: '{fileResource}'");
        }

        // For text documents with unrecognized extensions, try to find a factory that can handle them
        // This allows CodeEditorFactory to handle arbitrary text files on Windows via TextBinarySniffer
        if (viewType == DocumentViewType.TextDocument)
        {
            // Check all factories to see if any can handle this text file
            foreach (var factory in _documentEditorRegistry.GetAllFactories().OrderBy(f => f.Priority))
            {
                if (factory.CanHandle(fileResource, filePath))
                {
                    var createResult = factory.CreateDocumentView(fileResource);
                    if (createResult.IsSuccess)
                    {
                        return createResult;
                    }
                }
            }

            // Ultimate fallback to TextBoxDocumentView
            var textBoxView = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
            return Result<IDocumentView>.Ok(textBoxView);
        }

        return Result<IDocumentView>.Fail($"Failed to create document view for file: '{fileResource}'");
    }

    private void OnDocumentResourceChangedMessage(object recipient, DocumentResourceChangedMessage message)
    {
        var oldResource = message.OldResource.ToString();
        var newResource = message.NewResource.ToString();

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var newResourcePath = resourceRegistry.GetResourcePath(message.NewResource);

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
