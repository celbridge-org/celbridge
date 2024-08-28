using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentsService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    internal IDocumentsManager? DocumentsManager { get; set; }

    public DocumentsService(
        IServiceProvider serviceProvider,
        ILogger<DocumentsService> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public object CreateDocumentsPanel()
    {
        return _serviceProvider.GetRequiredService<DocumentsPanel>();
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsManager);

        var openResult = await DocumentsManager.OpenDocument(fileResource);
        if (openResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to open document for file resource '{fileResource}'");
            failure.MergeErrors(openResult);
            return failure;
        }

        return Result.Ok();
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsManager);

        var closeResult = await DocumentsManager.CloseDocument(fileResource);
        if (closeResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to close document for file resource '{fileResource}'");
            failure.MergeErrors(closeResult);
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
            }

            _disposed = true;
        }
    }

    ~DocumentsService()
    {
        Dispose(false);
    }
}
