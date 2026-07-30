using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    public IConsoleSessionService Sessions { get; }

    public IConsoleProcessOwner ProcessOwner { get; }

    public ConsoleService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        ProcessOwner = serviceProvider.AcquireService<IConsoleProcessOwner>();
        Sessions = serviceProvider.AcquireService<IConsoleSessionService>();
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
                // Sessions first so their processes are released before the owner goes.
                (Sessions as IDisposable)?.Dispose();
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
