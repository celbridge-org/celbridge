using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

public class ConsoleService : IConsoleService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;

    private IConsolePanel? _consolePanel;
    public IConsolePanel ConsolePanel => _consolePanel!;

    public ITerminal Terminal { get; private set; }

    public ConsoleService(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _messengerService = messengerService;

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);

        Terminal = serviceProvider.AcquireService<ITerminal>();
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _consolePanel = _serviceProvider.GetRequiredService<IConsolePanel>();
    }

    public async Task<Result> InitializeTerminalWindow()
    {
        Guard.IsNotNull(_consolePanel);

        return await _consolePanel.InitializeTerminalWindow(Terminal);
    }

    public void RunCommand(string command)
    {
        // Populate the CommandBuffer with the command to be executed.
        Terminal.CommandBuffer = command;

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
                _messengerService.UnregisterAll(this);
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
