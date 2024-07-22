using Celbridge.Documents.Views;

namespace Celbridge.Documents.Services;

public class DocumentsService : IDocumentsService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;

    public DocumentsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateDocumentsPanel()
    {
        return _serviceProvider.GetRequiredService<DocumentsPanel>();
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
