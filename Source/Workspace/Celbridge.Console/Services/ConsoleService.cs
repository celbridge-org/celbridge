using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ITerminal Terminal { get; private set; }

    public ConsoleService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _workspaceWrapper = workspaceWrapper;
        Terminal = serviceProvider.AcquireService<ITerminal>();
    }

    public async Task<Result> InitializeTerminalWindow()
    {
        var consolePanel = _workspaceWrapper.WorkspaceService.ConsolePanel;
        if (consolePanel is null)
        {
            return Result.Fail("Console panel is not available");
        }

        return await consolePanel.InitializeTerminalWindow(Terminal);
    }

    public void RunCommand(string command)
    {
        var consolePanel = _workspaceWrapper.WorkspaceService.ConsolePanel;
        if (consolePanel is null)
        {
            return;
        }

        consolePanel.RunCommand(command);
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
                Terminal?.Dispose();
            }

            _disposed = true;
        }
    }

    ~ConsoleService()
    {
        Dispose(false);
    }
}
