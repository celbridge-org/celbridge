using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private readonly ILogger<DocumentsService> _logger;
    private readonly IEditorSettings _editorSettings;
    private readonly ICommandService _commandService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public IDocumentsPanel? DocumentsPanel { get; private set; }

    public DocumentsService(
        ILogger<DocumentsService> logger,
        IEditorSettings editorSettings,
        ICommandService commandService,
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _editorSettings = editorSettings;
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

    public Result OpenPreviousDocuments()
    {
        Guard.IsNotNull(DocumentsPanel);

        var previousDocuments = _editorSettings.PreviousOpenDocuments;
        if (previousDocuments.Count == 0)
        {
            return Result.Ok();
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

        foreach (var document in previousDocuments)
        {
            if (!ResourceKey.IsValidKey(document))
            {
                // An invalid resource key was saved in the settings somehow.
                continue;
            }

            var fileResource = new ResourceKey(document);
            var getResult = resourceRegistry.GetResource(fileResource);
            if (getResult.IsFailure)
            {
                // This resource no longer exists so we can't open it again.
                continue;
            }

            // Execute a command to load the document
            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = fileResource;
            });
        }

        var selectedDocument = _editorSettings.PreviousSelectedDocument;
        if (ResourceKey.IsValidKey(selectedDocument))
        {
            var fileResource = new ResourceKey(selectedDocument);

            // Execute a command to select the previously selected document
            _commandService.Execute<ISelectDocumentCommand>(command =>
            {
                command.FileResource = fileResource;
            });
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
