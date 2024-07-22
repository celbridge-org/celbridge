using Celbridge.Console;
using Celbridge.Console.Views;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;

    public ConsoleService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateConsolePanel()
    {
        return _serviceProvider.GetRequiredService<ConsolePanel>();
    }

    public ICommandHistory CreateCommandHistory()
    {
        return new CommandHistory();
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
            }

            _disposed = true;
        }
    }

    ~ConsoleService()
    {
        Dispose(false);
    }
}
