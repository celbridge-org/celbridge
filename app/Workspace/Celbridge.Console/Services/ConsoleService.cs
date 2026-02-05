using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    public IConsolePanel ConsolePanel { get; set; } = null!;

    public ITerminal Terminal { get; private set; }

    public ConsoleService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        Terminal = serviceProvider.AcquireService<ITerminal>();
    }

    public async Task<Result> InitializeTerminalWindow()
    {
        Guard.IsNotNull(ConsolePanel); Guard.IsNotNull(ConsolePanel);

        return await ConsolePanel.InitializeTerminalWindow(Terminal);
    }

    public void RunCommand(string command)
    {
        // Populate the CommandBuffer with the command to be executed.
        var trimmedCommand = command.Trim();
        Terminal.CommandBuffer = trimmedCommand;

        // Send a fake keyboard interrupt to clear the current input buffer.
        // The terminal will inject the buffered command once the input buffer has been cleared.
        var interruptCode = $"{(char)3}";
        Terminal.Write(interruptCode);
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
