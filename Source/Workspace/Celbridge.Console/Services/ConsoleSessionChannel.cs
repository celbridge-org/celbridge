using Celbridge.Logging;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

/// <summary>
/// The console document's channel: it owns a per-view pty, bridges terminal I/O to the WebView over
/// console/* RPC, and (re)launches the session on a console/start request. The launch is JS-triggered so
/// the web app has registered its console/write handler before any output arrives.
/// </summary>
internal sealed class ConsoleSessionChannel : ICustomEditorChannel, IConsoleSessionRpc
{
    private readonly ILogger<ConsoleSessionChannel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceKey _fileResource;

    private ICustomEditorChannelHost? _host;
    private ITerminal? _terminal;
    private bool _disposed;

    public ConsoleSessionChannel(
        IServiceProvider serviceProvider,
        ResourceKey fileResource)
    {
        _serviceProvider = serviceProvider;
        _fileResource = fileResource;
        _logger = serviceProvider.GetRequiredService<ILogger<ConsoleSessionChannel>>();
        _workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
    }

    public void RegisterTargets(ICustomEditorChannelHost host)
    {
        _host = host;
        host.AddLocalRpcTarget<IConsoleSessionRpc>(this);
    }

    public void OnInput(string data)
    {
        _terminal?.Write(data);
    }

    public void OnResize(int cols, int rows)
    {
        _terminal?.SetSize(cols, rows);
    }

    public async Task<ConsoleStartResult> StartAsync(int cols, int rows, ConsoleConfigDto config)
    {
        // A start request also serves as reopen: dispose any running pty and launch fresh from the current
        // config, keeping the tab and WebView.
        DisposeTerminal();

        var typeId = string.IsNullOrWhiteSpace(config.Type) ? "shell" : config.Type;

        IConsoleSessionProvider? provider = null;
        foreach (var candidate in _serviceProvider.GetServices<IConsoleSessionProvider>())
        {
            if (candidate.TypeId == typeId)
            {
                provider = candidate;
                break;
            }
        }

        if (provider is null)
        {
            return new ConsoleStartResult(false, $"Unknown console session type '{typeId}'.");
        }

        var projectFolderPath = _workspaceWrapper.WorkspaceService.ResourceService.Registry.ProjectFolderPath;

        var sessionContext = new ConsoleSessionContext(
            _fileResource,
            typeId,
            config.Executable ?? string.Empty,
            config.Arguments ?? Array.Empty<string>(),
            config.WorkingDirectory ?? string.Empty,
            config.Environment ?? new Dictionary<string, string>(),
            projectFolderPath);

        var specResult = await provider.BuildLaunchSpecAsync(sessionContext);
        if (specResult.IsFailure)
        {
            return new ConsoleStartResult(false, specResult.FirstErrorMessage);
        }

        var launchSpec = specResult.Value;

        var terminal = _serviceProvider.GetRequiredService<ITerminal>();
        terminal.OutputReceived += OnTerminalOutput;
        terminal.ProcessExited += OnTerminalProcessExited;

        // Size the pty before it starts so the shell paints at the WebView's geometry rather than 80x25.
        terminal.SetSize(cols, rows);

        try
        {
            var environment = new Dictionary<string, string>(launchSpec.Environment);
            terminal.Start(launchSpec.CommandLine, launchSpec.WorkingDirectory, environment);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start the console session");
            terminal.OutputReceived -= OnTerminalOutput;
            terminal.ProcessExited -= OnTerminalProcessExited;
            terminal.Dispose();
            return new ConsoleStartResult(false, exception.Message);
        }

        _terminal = terminal;
        return new ConsoleStartResult(true, null);
    }

    private void OnTerminalOutput(object? sender, string output)
    {
        if (_disposed)
        {
            return;
        }

        // The pty read loop is a single background thread, so notifications stay ordered and StreamJsonRpc
        // serializes the writes. The adapter no-ops if the host has been torn down.
        _ = _host?.NotifyAsync(ConsoleSessionRpcMethods.Write, new { text = output });
    }

    private void OnTerminalProcessExited(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = _host?.NotifyAsync(ConsoleSessionRpcMethods.SessionState, new { state = "ended" });
    }

    private void DisposeTerminal()
    {
        var terminal = _terminal;
        if (terminal is null)
        {
            return;
        }

        _terminal = null;
        terminal.OutputReceived -= OnTerminalOutput;
        terminal.ProcessExited -= OnTerminalProcessExited;
        terminal.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeTerminal();
        _host = null;
    }
}
