using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    public IConsoleSessionRegistry SessionRegistry { get; }

    public IConsoleProcessOwner ProcessOwner { get; }

    public ConsoleService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        SessionRegistry = serviceProvider.AcquireService<IConsoleSessionRegistry>();
        ProcessOwner = serviceProvider.AcquireService<IConsoleProcessOwner>();
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
                (SessionRegistry as IDisposable)?.Dispose();
                (ProcessOwner as IDisposable)?.Dispose();
            }

            _disposed = true;
        }
    }

    ~ConsoleService()
    {
        Dispose(false);
    }
}
