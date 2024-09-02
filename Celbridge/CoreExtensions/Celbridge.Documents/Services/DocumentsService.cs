using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private const string PreviousOpenDocumentsKey = "PreviousOpenDocuments";
    private const string PreviousSelectedDocumentKey = "PreviousSelectedDocument";

    private readonly ILogger<DocumentsService> _logger;
    private readonly ICommandService _commandService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public IDocumentsPanel? DocumentsPanel { get; private set; }

    public DocumentsService(
        ILogger<DocumentsService> logger,
        ICommandService commandService,
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _commandService = commandService;
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public IDocumentsPanel CreateDocumentsPanel()
    {
        DocumentsPanel = _serviceProvider.GetRequiredService<DocumentsPanel>();
        return DocumentsPanel;
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsPanel);

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var filePath = resourceRegistry.GetResourcePath(fileResource);
        if (string.IsNullOrEmpty(filePath) ||
            !File.Exists(filePath))
        {
            return Result.Fail($"File path does not exist: '{filePath}'");
        }

        var openResult = await DocumentsPanel.OpenDocument(fileResource, filePath);
        if (openResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to open document for file resource '{fileResource}'");
            failure.MergeErrors(openResult);
            return failure;
        }

        _logger.LogTrace($"Opened document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose)
    {
        Guard.IsNotNull(DocumentsPanel);

        var closeResult = await DocumentsPanel.CloseDocument(fileResource, forceClose);
        if (closeResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to close document for file resource '{fileResource}'");
            failure.MergeErrors(closeResult);
            return failure;
        }

        _logger.LogTrace($"Closed document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public Result SelectDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsPanel);

        var selectResult = DocumentsPanel.SelectDocument(fileResource);
        if (selectResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to select opened document for file resource '{fileResource}'");
            failure.MergeErrors(selectResult);
            return failure;
        }

        _logger.LogTrace($"Selected document for file resource '{fileResource}'");

        return Result.Ok();
    }

    public async Task<Result> SaveModifiedDocuments(double deltaTime)
    {
        Guard.IsNotNull(DocumentsPanel);

        var saveResult = await DocumentsPanel.SaveModifiedDocuments(deltaTime);
        if (saveResult.IsFailure)
        {
            var failure = Result.Fail("Failed to save modified documents");
            failure.MergeErrors(saveResult);
            return failure;
        }

        return Result.Ok();
    }

    public async Task StoreOpenDocuments(List<ResourceKey> openDocuments)
    {
        var workspaceData = _workspaceWrapper.WorkspaceService.WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        List<string> documents = [];
        foreach (var fileResource in openDocuments)
        {
            documents.Add(fileResource.ToString());
        }

        await workspaceData.SetPropertyAsync(PreviousOpenDocumentsKey, documents);
    }

    public async Task StoreSelectedDocument(ResourceKey selectedDocument)
    {
        var workspaceData = _workspaceWrapper.WorkspaceService.WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        var fileResource = selectedDocument.ToString();

        await workspaceData.SetPropertyAsync(PreviousSelectedDocumentKey, fileResource);
    }

    public async Task RestoreDocuments()
    {
        Guard.IsNotNull(DocumentsPanel);

        var workspaceData = _workspaceWrapper.WorkspaceService.WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        var openDocuments = await workspaceData.GetPropertyAsync<List<string>>(PreviousOpenDocumentsKey);
        if (openDocuments is null ||
            openDocuments.Count == 0)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

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
            // Use ExecuteNow() to ensure the command is executed while the workspace is still loading.
            var openResult = await _commandService.ExecuteNow<IOpenDocumentCommand>(command =>
            {
                command.FileResource = fileResource;
            });

            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open previously open document '{fileResource}'");
            }
        }

        var selectedDocument = await workspaceData.GetPropertyAsync<string>(PreviousSelectedDocumentKey);
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
        // Use ExecuteNow() to ensure the command is executed while the workspace is still loading.
        var selectResult = await _commandService.ExecuteNow<ISelectDocumentCommand>(command =>
        {
            command.FileResource = new ResourceKey(selectedDocument);
        });

        if (selectResult.IsFailure)
        {
            _logger.LogWarning($"Failed to select previously selected document '{selectedDocument}'");
        }
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
                _messengerService.Unregister<ResourceRegistryUpdatedMessage>(this);
            }

            _disposed = true;
        }
    }

    ~DocumentsService()
    {
        Dispose(false);
    }
}
