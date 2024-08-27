using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentsService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

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
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(fileResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"File resource not found: '{fileResource}'");
        }

        var resource = getResult.Value as IFileResource;
        if (resource is null)
        {
            return Result.Fail($"Resource is not a file: '{fileResource}'");
        }

        _logger.LogInformation($"Opening file resource document '{fileResource}'");

        await Task.CompletedTask;

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
