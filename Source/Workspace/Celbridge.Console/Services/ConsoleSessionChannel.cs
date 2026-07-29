using Celbridge.Logging;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

/// <summary>
/// The console document's channel: it owns a per-view pty, bridges terminal I/O to the WebView over
/// console/* RPC, registers the console with the session registry, and (re)launches the session on a
/// console/start request. The launch is JS-triggered so the web app has registered its console/write
/// handler before any output arrives.
/// </summary>
internal sealed class ConsoleSessionChannel : ICustomEditorChannel, IConsoleSessionRpc, IConsoleCommandInjector
{
    private readonly ILogger<ConsoleSessionChannel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceKey _fileResource;

    private ICustomEditorChannelHost? _host;
    private ITerminal? _terminal;
    private int? _trackedProcessId;
    private bool _registered;
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

        var provider = ResolveProvider(typeId);
        if (provider is null)
        {
            return new ConsoleStartResult(false, $"Unknown console session type '{typeId}'.");
        }

        var registry = _workspaceWrapper.WorkspaceService.ConsoleService.SessionRegistry;

        // Register (or on reopen re-register) the console with its effective runners, so the Run menu can
        // target it, and to obtain the session token a connecting client echoes back via session/handshake.
        var runners = ResolveRunners(config, provider);
        var registration = new ConsoleRegistration(
            _fileResource,
            typeId,
            config.Title ?? string.Empty,
            runners,
            this);
        var session = registry.Register(registration);
        _registered = true;

        // Seed the host-connection variables for every console: the shared listener port every peer dials,
        // and this console's session token. A shell console passes them on to anything it launches (a
        // typed celbridge-py, a spawned terminal), so any child can dial back and attribute itself to
        // this console.
        var environment = new Dictionary<string, string>(config.Environment ?? new Dictionary<string, string>());
        var rpcPort = await registry.EnsureRpcListenerAsync();
        environment[ConsoleEnvironmentVariables.RpcPort] = rpcPort.ToString();
        environment[ConsoleEnvironmentVariables.SessionToken] = session.SessionId.ToString();

        var projectFolderPath = _workspaceWrapper.WorkspaceService.ResourceService.Registry.ProjectFolderPath;

        var sessionContext = new ConsoleSessionContext(
            _fileResource,
            typeId,
            config.Executable ?? string.Empty,
            config.Arguments ?? Array.Empty<string>(),
            config.WorkingDirectory ?? string.Empty,
            environment,
            projectFolderPath,
            config.Dependencies,
            config.PythonVersion);

        // A provider throw (e.g. file IO while resolving a Python launch) must surface as a session-failed
        // result in the document, not as a protocol-level RPC error.
        Result<ConsoleLaunchSpec> specResult;
        try
        {
            specResult = await provider.BuildLaunchSpecAsync(sessionContext);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to build the console launch spec");
            specResult = Result<ConsoleLaunchSpec>.Fail(exception.Message);
        }

        if (specResult.IsFailure)
        {
            registry.Unregister(_fileResource);
            _registered = false;
            return new ConsoleStartResult(false, specResult.FirstErrorMessage);
        }

        var launchSpec = specResult.Value;

        var terminal = _serviceProvider.GetRequiredService<ITerminal>();
        terminal.OutputReceived += OnTerminalOutput;
        terminal.ProcessExited += OnTerminalProcessExited;

        // Size the pty before it starts so the shell paints at the WebView's geometry rather than 80x25.
        terminal.SetSize(cols, rows);

        // Contributors amend the final environment for every console type, e.g. Python putting the uv tool
        // bin folder on PATH so celbridge-py resolves in a shell console.
        var environmentCopy = new Dictionary<string, string>(launchSpec.Environment);
        foreach (var contributor in _serviceProvider.GetServices<IConsoleEnvironmentContributor>())
        {
            try
            {
                await contributor.ContributeAsync(sessionContext, environmentCopy);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A console environment contributor failed; launching without its variables");
            }
        }

        try
        {
            terminal.Start(launchSpec.CommandLine, launchSpec.WorkingDirectory, environmentCopy);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start the console session");
            terminal.OutputReceived -= OnTerminalOutput;
            terminal.ProcessExited -= OnTerminalProcessExited;
            terminal.Dispose();
            registry.Unregister(_fileResource);
            _registered = false;
            return new ConsoleStartResult(false, exception.Message);
        }

        _terminal = terminal;

        // Track the child so the workspace-scoped owner tears it down on project close or app crash.
        if (terminal.ProcessId is int processId)
        {
            _trackedProcessId = processId;
            _workspaceWrapper.WorkspaceService.ConsoleService.ProcessOwner.Track(processId);
        }

        return new ConsoleStartResult(true, null);
    }

    private IConsoleSessionProvider? ResolveProvider(string typeId)
    {
        foreach (var candidate in _serviceProvider.GetServices<IConsoleSessionProvider>())
        {
            if (candidate.TypeId == typeId)
            {
                return candidate;
            }
        }

        return null;
    }

    public void InjectCommand(string text)
    {
        // Clear any partial input (Ctrl+U, U+0015) before submitting, so a run command or shortcut is not
        // concatenated with whatever the user had half-typed at the prompt.
        _terminal?.Write("\u0015" + text + "\r");
    }

    private static IReadOnlyList<ConsoleRunner> ResolveRunners(ConsoleConfigDto config, IConsoleSessionProvider provider)
    {
        // Any runners in the config replace the type defaults outright. An empty list falls back to them.
        var configuredRunners = config.Runners;
        if (configuredRunners is null || configuredRunners.Count == 0)
        {
            return provider.DefaultRunners;
        }

        var runners = new List<ConsoleRunner>();
        foreach (var runnerDto in configuredRunners)
        {
            var extensions = runnerDto.Extensions ?? Array.Empty<string>();
            var command = runnerDto.Command ?? string.Empty;
            if (extensions.Count == 0 || string.IsNullOrWhiteSpace(command))
            {
                continue;
            }
            runners.Add(new ConsoleRunner(extensions, command));
        }

        return runners;
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

        if (_workspaceWrapper.HasWorkspaceService)
        {
            _workspaceWrapper.WorkspaceService.ConsoleService.SessionRegistry.SetState(_fileResource, ConsoleSessionState.Ended);
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

        // The pty has killed its child, so stop tracking it (the id could be reused).
        if (_trackedProcessId is int processId)
        {
            _trackedProcessId = null;
            if (_workspaceWrapper.HasWorkspaceService)
            {
                _workspaceWrapper.WorkspaceService.ConsoleService.ProcessOwner.Untrack(processId);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeTerminal();

        if (_registered
            && _workspaceWrapper.HasWorkspaceService)
        {
            _workspaceWrapper.WorkspaceService.ConsoleService.SessionRegistry.Unregister(_fileResource);
        }

        _host = null;
    }
}
