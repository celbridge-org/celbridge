using Celbridge.Inspector.Views;

namespace Celbridge.Inspector.Services;

public class InspectorService : IInspectorService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;

    public InspectorService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateInspectorPanel()
    {
        return _serviceProvider.GetRequiredService<InspectorPanel>();
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

    ~InspectorService()
    {
        Dispose(false);
    }
}
