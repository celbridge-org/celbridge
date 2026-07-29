using Celbridge.Console.Helpers;
using Celbridge.Logging;
using Celbridge.Utilities;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

/// <summary>
/// The console document's channel: it owns a per-view pty running the platform shell, bridges terminal
/// I/O to the WebView over console/* RPC, registers the console with the session registry, and
/// (re)launches the session on a console/start request, injecting the session type's startup command once
/// the shell is up. The launch is JS-triggered so the web app has registered its console/write handler
/// before any output arrives.
/// </summary>
internal sealed class ConsoleSessionChannel : ICustomEditorChannel, IConsoleSessionRpc, IConsoleCommandInjector
{
    // Prefixes the ready marker the injected command echoes once it has cleared the screen. The random
    // per-launch suffix guards only against a stale marker from a previous launch arriving late on the
    // old pty's stream and revealing the new session early. What stops the shell's own echo of the
    // injected line matching is the literal split in ShellCommandComposer, not this suffix.
    private const string ReadyMarkerPrefix = "CELBRIDGE-CONSOLE-READY-";

    private readonly ILogger<ConsoleSessionChannel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceKey _fileResource;

    private ICustomEditorChannelHost? _host;
    private ITerminal? _terminal;
    private StartupInjector? _startupInjector;
    private int? _trackedProcessId;
    private bool _registered;
    private bool _disposed;

    // While the startup command is pending injection, raw user input is dropped (it would corrupt the
    // command line at the shell prompt) and programmatic injections are buffered to flush afterwards.
    private readonly object _injectionGateLock = new();
    private readonly List<string> _bufferedInjections = new();
    private volatile bool _startupInjectionPending;

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
        if (_startupInjectionPending)
        {
            return;
        }

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
            config.PythonVersion,
            config.StartupScript);

        // A provider throw (e.g. file IO while resolving a Python launch) must surface as a session-failed
        // result in the document, not as a protocol-level RPC error.
        Result<ConsoleStartupInvocation> commandResult;
        try
        {
            commandResult = await provider.BuildStartupInvocationAsync(sessionContext);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to build the console startup command");
            commandResult = Result<ConsoleStartupInvocation>.Fail(exception.Message);
        }

        if (commandResult.IsFailure)
        {
            registry.Unregister(_fileResource);
            _registered = false;
            return new ConsoleStartResult(false, commandResult.FirstErrorMessage);
        }

        var startupInvocation = commandResult.Value;

        // Every session runs the platform shell; the session type only decides what is injected into it.
        // The injected line clears the shell-startup noise and echoes a ready marker, so the document
        // reveals the terminal exactly when the screen is clear rather than while the shell is still
        // echoing the command.
        var shell = ConsoleShell.Resolve();
        var shellCommandLine = new CommandLineBuilder(shell.Executable).ToString();

        string? readyMarker = null;
        if (ShellCommandComposer.SupportsReadyMarker(shell.Family))
        {
            readyMarker = ReadyMarkerPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        var injectedCommandLine = ShellCommandComposer.Compose(shell.Family, startupInvocation, readyMarker);
        var hasStartupCommand = !string.IsNullOrEmpty(injectedCommandLine);

        // The type's command runs first, then the user's startup script rides the same type-ahead buffer,
        // so the script reaches whatever the command left owning the prompt: a REPL for a python console,
        // the shell itself for a plain one.
        var injectedLines = new List<string>();
        if (hasStartupCommand)
        {
            injectedLines.Add(injectedCommandLine);
        }
        if (!startupInvocation.HandlesStartupScript)
        {
            injectedLines.AddRange(ConsoleStartupScript.SplitLines(config.StartupScript));
        }

        var workingDirectory = ConsoleWorkingFolder.Resolve(sessionContext.WorkingDirectory, projectFolderPath);

        var terminal = _serviceProvider.GetRequiredService<ITerminal>();
        terminal.OutputReceived += OnTerminalOutput;
        terminal.ProcessExited += OnTerminalProcessExited;

        // Size the pty before it starts so the shell paints at the WebView's geometry rather than 80x25.
        terminal.SetSize(cols, rows);

        var environmentCopy = new Dictionary<string, string>(environment);

        // The startup command's own environment (e.g. the python launch defaults a retyped celbridge-py
        // reads) merges add-if-absent, so the console's [session.environment] still wins.
        if (startupInvocation.Environment is not null)
        {
            foreach (var pair in startupInvocation.Environment)
            {
                if (!environmentCopy.ContainsKey(pair.Key))
                {
                    environmentCopy[pair.Key] = pair.Value;
                }
            }
        }

        // Contributors amend the final environment for every console type, e.g. Python putting the uv tool
        // bin folder on PATH so celbridge-py resolves in a shell console.
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

        // Gate input before the pty starts, so nothing typed can reach the shell prompt ahead of the
        // injected lines, or interleave between them.
        _startupInjectionPending = injectedLines.Count > 0;

        try
        {
            terminal.Start(shellCommandLine, workingDirectory, environmentCopy);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start the console session");
            _startupInjectionPending = false;
            terminal.OutputReceived -= OnTerminalOutput;
            terminal.ProcessExited -= OnTerminalProcessExited;
            terminal.Dispose();
            registry.Unregister(_fileResource);
            _registered = false;
            return new ConsoleStartResult(false, exception.Message);
        }

        _terminal = terminal;

        if (injectedLines.Count > 0)
        {
            _startupInjector = StartupInjector.Begin(terminal, injectedLines, CompleteStartup);
        }

        // Track the child so the workspace-scoped owner tears it down on project close or app crash.
        if (terminal.ProcessId is int processId)
        {
            _trackedProcessId = processId;
            _workspaceWrapper.WorkspaceService.ConsoleService.ProcessOwner.Track(processId);
        }

        return new ConsoleStartResult(
            true,
            null,
            hasStartupCommand,
            hasStartupCommand ? readyMarker : null);
    }

    // Ends the startup phase, on the injector's worker once the startup command has been written: the
    // input gate reopens, buffered programmatic injections flush behind the command, and the document is
    // told no further host milestones are coming.
    private void CompleteStartup()
    {
        List<string> bufferedInjections;
        lock (_injectionGateLock)
        {
            _startupInjectionPending = false;
            bufferedInjections = new List<string>(_bufferedInjections);
            _bufferedInjections.Clear();
        }

        foreach (var text in bufferedInjections)
        {
            _terminal?.Write(text);
        }

        _ = _host?.NotifyAsync(ConsoleSessionRpcMethods.StartupComplete, new { });
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
        var submission = "\u0015" + text + "\r";

        // A programmatic injection during startup queues behind the startup command rather than racing
        // it, so a Run issued at console open still lands as type-ahead for the starting REPL.
        lock (_injectionGateLock)
        {
            if (_startupInjectionPending)
            {
                _bufferedInjections.Add(submission);
                return;
            }
        }

        _terminal?.Write(submission);
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
        _startupInjector?.Dispose();
        _startupInjector = null;

        lock (_injectionGateLock)
        {
            _startupInjectionPending = false;
            _bufferedInjections.Clear();
        }

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
