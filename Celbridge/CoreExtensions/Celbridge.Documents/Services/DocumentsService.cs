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

    public async Task<Result> OpenFileDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsManager);

        var openResult = await DocumentsManager.OpenFileDocument(fileResource);
        if (openResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to open file resource '{fileResource}'");
            failure.MergeErrors(openResult);
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
