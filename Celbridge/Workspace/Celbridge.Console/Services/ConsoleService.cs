using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;

    private IConsolePanel? _consolePanel;
    public IConsolePanel ConsolePanel => _consolePanel!;

    public ConsoleService(
        IServiceProvider serviceProvider,
        IMessengerService messengerService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _consolePanel = _serviceProvider.GetRequiredService<IConsolePanel>();
    }

    public event Action<MessageType, string>? OnPrint;

    public void Print(MessageType printType, string message)
    {
        OnPrint?.Invoke(printType, message);
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
            }

            _disposed = true;
        }
    }

    ~ConsoleService()
    {
        Dispose(false);
    }
}
