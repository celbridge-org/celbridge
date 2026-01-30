using System.Text.RegularExpressions;
using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public record SetTextDocumentContentMessage(ResourceKey Resource, string Content);

public class DocumentsService : IDocumentsService, IDisposable
{
    private const string OpenDocumentsKey = "OpenDocuments";
    private const string SelectedDocumentKey = "SelectedDocument";
    private const string SectionRatiosKey = "SectionRatios";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentsService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private IDocumentsPanel? _documentsPanel;
    public IDocumentsPanel DocumentsPanel => _documentsPanel!;

    public ResourceKey SelectedDocument { get; private set; }

    public List<ResourceKey> OpenDocuments { get; } = new();

    // This utility is only used internally and is not exposed via IDocumentService
    internal TextEditorWebViewPool TextEditorWebViewPool { get; }

    private bool _isWorkspaceLoaded;

    private FileTypeHelper _fileTypeHelper;

    private Dictionary<string, IPreviewProvider> _previewProviders = new();

    public DocumentsService(
        IServiceProvider serviceProvider,
        ILogger<DocumentsService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _logger = logger;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;

        // Initialize the TextEditorWebViewPool
        TextEditorWebViewPool = new TextEditorWebViewPool(3);

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<OpenDocumentsChangedMessage>(this, OnOpenDocumentsChangedMessage);
        _messengerService.Register<SelectedDocumentChangedMessage>(this, OnSelectedDocumentChangedMessage);
        _messengerService.Register<SectionRatiosChangedMessage>(this, OnSectionRatiosChangedMessage);
        _messengerService.Register<DocumentResourceChangedMessage>(this, OnDocumentResourceChangedMessage);

        _fileTypeHelper = _serviceProvider.GetRequiredService<FileTypeHelper>();
        var loadResult = _fileTypeHelper.Initialize();
        if (loadResult.IsFailure)
        {
            throw new InvalidProgramException("Failed to initialize file type helper");
        }
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _documentsPanel = _serviceProvider.GetRequiredService<IDocumentsPanel>();
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        // Once set, this will remain true for the lifetime of the service
        _isWorkspaceLoaded = true;
    }

    private void OnSelectedDocumentChangedMessage(object recipient, SelectedDocumentChangedMessage message)
    {
        SelectedDocument = message.DocumentResource;

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreSelectedDocument();
        }
    }

    private void OnOpenDocumentsChangedMessage(object recipient, OpenDocumentsChangedMessage message)
    {
        OpenDocuments.ReplaceWith(message.OpenDocuments);

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreOpenDocuments();
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

    public async Task<Result> OpenDocument(ResourceKey fileResource, bool forceReload)
    {
        return await OpenDocument(fileResource, forceReload, string.Empty);
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource, bool forceReload, string location)
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

        var openResult = await DocumentsPanel.OpenDocument(fileResource, filePath, forceReload, location);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open document for file resource '{fileResource}'")
                .WithErrors(openResult);
        }

        _logger.LogTrace($"Opened document for file resource '{fileResource}'");

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

    public Result SelectDocument(ResourceKey fileResource)
    {
        var selectResult = DocumentsPanel.SelectDocument(fileResource);
        if (selectResult.IsFailure)
        {
            return Result.Fail($"Failed to select opened document for file resource '{fileResource}'")
                .WithErrors(selectResult);
        }

        _logger.LogTrace($"Selected document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public async Task<Result> SetTextDocumentContentAsync(ResourceKey fileResource, string content)
    {
        try
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

            var filePath = resourceRegistry.GetResourcePath(fileResource);

            // Udpate the document file with the new content
            await File.WriteAllTextAsync(filePath, content);

            // Update the document view with the new content
            var message = new SetTextDocumentContentMessage(fileResource, content);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set text document content")
                .WithException(ex);
        }
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

    public async Task StoreOpenDocuments()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        List<string> documents = [];
        foreach (var fileResource in OpenDocuments)
        {
            documents.Add(fileResource.ToString());
        }

        await workspaceSettings.SetPropertyAsync(OpenDocumentsKey, documents);
    }

    public async Task StoreSelectedDocument()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var fileResource = SelectedDocument.ToString();

        await workspaceSettings.SetPropertyAsync(SelectedDocumentKey, fileResource);
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
            _documentsPanel!.SectionCount = sectionRatios.Count;
            _documentsPanel!.SetSectionRatios(sectionRatios);
        }

        var openDocuments = await workspaceSettings.GetPropertyAsync<List<string>>(OpenDocumentsKey);
        if (openDocuments is null ||
            openDocuments.Count == 0)
        {
            // If no documents are currently open then try to open the default readme file.
            var readmeResource = new ResourceKey("readme.md");

            // Normalize the resource key to match the actual casing on disk
            var normalizeResult = resourceRegistry.NormalizeResourceKey(readmeResource);
            if (normalizeResult.IsSuccess)
            {
                var normalizedResource = normalizeResult.Value;
                var readmePath = resourceRegistry.GetResourcePath(normalizedResource);
                if (CanAccessFile(readmePath))
                {
                    // Todo: Force the file to open in preview mode (we want the user to read it, not edit it).

                    // Execute a command to open the readme file.
                    _commandService.Execute<IOpenDocumentCommand>(command =>
                    {
                        command.FileResource = normalizedResource;
                        command.ForceReload = false;
                    });
                }
            }

            return;
        }

        // Open the previously opened documents

        foreach (var resourceKey in openDocuments)
        {
            if (!ResourceKey.IsValidKey(resourceKey))
            {
                // An invalid resource key was saved in the settings somehow.
                _logger.LogWarning($"Invalid resource key '{resourceKey}' found in previously open documents");
                continue;
            }

            var fileResource = new ResourceKey(resourceKey);
            var getResourceResult = resourceRegistry.GetResource(fileResource);
            if (getResourceResult.IsFailure)
            {
                // This resource doesn't exist now so we can't open it again.
                _logger.LogWarning(getResourceResult, $"Failed to open document because '{fileResource}' resource does not exist.");
                continue;
            }

            // Execute a command to load the document
            // Use ExecuteImmediate() to ensure the command is executed while the workspace is still loading.
            var openResult = await _commandService.ExecuteImmediate<IOpenDocumentCommand>(command =>
            {
                command.FileResource = fileResource;
            });

            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open previously open document '{fileResource}'");
            }
        }

        var selectedDocument = await workspaceSettings.GetPropertyAsync<string>(SelectedDocumentKey);
        if (string.IsNullOrEmpty(selectedDocument))
        {
            return;
        }

        if (!ResourceKey.IsValidKey(selectedDocument))
        {
            _logger.LogWarning($"Invalid resource key '{selectedDocument}' found for previously selected document");
            return;
        }

        // Execute a command to select the previously selected document
        // Use ExecuteImmediate() to ensure the command is executed while the workspace is still loading.
        var selectResult = await _commandService.ExecuteImmediate<ISelectDocumentCommand>(command =>
        {
            command.FileResource = new ResourceKey(selectedDocument);
        });

        if (selectResult.IsFailure)
        {
            _logger.LogWarning($"Failed to select previously selected document '{selectedDocument}'");
        }
    }

    public Result AddPreviewProvider(string fileExtension, IPreviewProvider previewProvider)
    {
        // Check for valid file extension format
        if (!Regex.IsMatch(fileExtension, @"^\.[a-zA-Z0-9]+$"))
        {
            return Result.Fail($"Invalid file extension: '{fileExtension}'");
        }

        // Check for conflicts with previously registered providers
        if (_previewProviders.ContainsKey(fileExtension))
        {
            return Result.Fail($"A preview provider is already registered for extension: '{fileExtension}'");
        }

        _previewProviders.Add(fileExtension, previewProvider);

        return Result.Ok();
    }

    public Result<IPreviewProvider> GetPreviewProvider(string fileExtension)
    {
        if (_previewProviders.TryGetValue(fileExtension, out var previewProvider))
        {
            return Result<IPreviewProvider>.Ok(previewProvider);
        }

        return Result<IPreviewProvider>.Fail();
    }

    private Result<IDocumentView> CreateDocumentViewInternal(ResourceKey fileResource)
    {
        var viewType = GetDocumentViewType(fileResource);

        IDocumentView? documentView = null;
        switch (viewType)
        {
            case DocumentViewType.UnsupportedFormat:
                return Result<IDocumentView>.Fail($"File resource is not a supported document format: '{fileResource}'");

#if WINDOWS
            case DocumentViewType.TextDocument:
                documentView = _serviceProvider.GetRequiredService<TextEditorDocumentView>();
                break;

            case DocumentViewType.WebAppDocument:
                documentView = _serviceProvider.GetRequiredService<WebAppDocumentView>();
                break;

            case DocumentViewType.FileViewer:
                documentView = _serviceProvider.GetRequiredService<FileViewerDocumentView>();
                break;

            case DocumentViewType.Spreadsheet:
                documentView = _serviceProvider.GetRequiredService<SpreadsheetDocumentView>();
                break;
#else
            case DocumentViewType.WebAppDocument:
                documentView = _serviceProvider.GetRequiredService<WebAppDocumentView>();
                break;
            case DocumentViewType.TextDocument:
            case DocumentViewType.FileViewer:

                // On non-Windows platforms, use the text editor document view for all document types
                documentView = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
                break;
#endif

        }

        if (documentView is null)
        {
            return Result<IDocumentView>.Fail($"Failed to create document view for file: '{fileResource}'");
        }

        return Result<IDocumentView>.Ok(documentView);
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

                _previewProviders.Clear();

                TextEditorWebViewPool.Shutdown();
            }

            _disposed = true;
        }
    }

    ~DocumentsService()
    {
        Dispose(false);
    }
}
