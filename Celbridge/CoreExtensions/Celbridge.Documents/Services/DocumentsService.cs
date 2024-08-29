using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private readonly ILogger<DocumentsService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public IDocumentsPanel? DocumentsPanel { get; set; }

    public DocumentsService(
        ILogger<DocumentsService> logger,
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdated);
    }

    private void OnResourceRegistryUpdated(object recipient, ResourceRegistryUpdatedMessage message)
    {
        _ = CloseDeletedDocuments();
    }

    private async Task CloseDeletedDocuments()
    {
        Guard.IsNotNull(DocumentsPanel);

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

        // Get list of open documents
        var openDocuments = DocumentsPanel.GetOpenDocuments();
        foreach (var fileResource in openDocuments)
        {
            // Check if the open document is in the updated resource registry
            var getResult = resourceRegistry.GetResource(fileResource);
            if (getResult.IsFailure)
            {
                var closeResult = await CloseDocument(fileResource, true);
                if (closeResult.IsFailure)
                {
                    _logger.LogError(closeResult, $"Failed to close document for file resource: '{fileResource}'");
                }
            }
        }
    }

    public object CreateDocumentsPanel()
    {
        return _serviceProvider.GetRequiredService<DocumentsPanel>();
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsPanel);

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("No workspace is loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

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
