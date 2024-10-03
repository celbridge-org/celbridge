using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Inspector.Services;

public class InspectorService : IInspectorService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;

    private IInspectorPanel? _inspectorPanel;
    public IInspectorPanel InspectorPanel => _inspectorPanel!;

    public InspectorService(
        IServiceProvider serviceProvider,
        IMessengerService messengerService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _inspectorPanel = _serviceProvider.GetRequiredService<IInspectorPanel>();
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
                _messengerService.Unregister<WorkspaceWillPopulatePanelsMessage>(this);
            }

            _disposed = true;
        }
    }

    ~InspectorService()
    {
        Dispose(false);
    }
}
