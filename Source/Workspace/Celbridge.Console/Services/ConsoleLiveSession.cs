using Celbridge.Console.Helpers;
using Celbridge.Logging;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

/// <summary>
/// One running console session, owned by the session service independently of any document view. It owns the
/// pty, injects the startup lines, consumes the ready marker so its scrollback starts on a clean screen,
/// buffers output while no view is attached, and forwards output live to the attached view.
/// </summary>
internal sealed class ConsoleLiveSession : IDisposable
{
    // Carriage return, the submit key at a shell or REPL prompt.
    private const string SubmitKey = "\r";

    // How long after the invocation text the submit key is written. Long enough that a terminal app
    // grouping a burst of stdin does not take the two for one paste.
    private const int SubmitKeyDelayMs = 100;

    // How long the marker scan waits without any output before revealing what the runtime is printing.
    // Measured from the last output rather than from injection, because the expensive part of a launch
    // happens after the command is typed: a first run resolves an interpreter and installs packages, which
    // can take far longer than any fixed budget while still making progress.
    private const int MarkerSilenceTimeoutMs = 10000;

    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<ConsoleLiveSession> _logger;
    private readonly ConsoleOutputBuffer _outputBuffer = new();

    private readonly object _gateLock = new();
    private readonly List<string> _bufferedInjections = new();
    private volatile bool _startupInjectionPending;

    private readonly object _streamLock = new();
    private readonly DiagnosticSequenceScanner _diagnosticScanner = new();
    private StartupMarkerScanner? _markerScanner;
    private bool _markerPersistsOnScreen;
    private bool _markerSeen;
    private bool _markerRevealed;
    private IConsoleView? _attachedView;

    private ITerminal? _terminal;
    private StartupInjector? _startupInjector;
    private List<string>? _deferredInjectionLines;
    private Timer? _markerTimeout;
    private int? _trackedProcessId;
    private bool _disposed;

    public ConsoleLiveSession(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        ResourceKey resource)
    {
        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
        Resource = resource;
        _logger = serviceProvider.GetRequiredService<ILogger<ConsoleLiveSession>>();
    }

    public ResourceKey Resource { get; private set; }

    public ConsoleSessionRunState State { get; private set; } = ConsoleSessionRunState.Starting;

    public string? Error { get; private set; }

    public string? LaunchedConfigToml { get; private set; }

    /// <summary>
    /// Regenerated on each launch and seeded into the session environment as the handshake token, so a
    /// connecting client can be attributed to this session.
    /// </summary>
    public Guid SessionId { get; private set; } = Guid.NewGuid();

    public string TypeId { get; private set; } = string.Empty;

    public IReadOnlyList<ConsoleRunner> Runners { get; private set; } = Array.Empty<ConsoleRunner>();

    /// <summary>
    /// The triggers this launch is watching, with their patterns already compiled.
    /// </summary>
    public IReadOnlyList<ConsoleTrigger> Triggers { get; private set; } = Array.Empty<ConsoleTrigger>();

    /// <summary>
    /// The bound client connection, or null when no client is connected.
    /// </summary>
    public int? ConnectionId { get; set; }

    /// <summary>
    /// Whether a client has connected at any point during this launch.
    /// </summary>
    public bool HasConnected { get; set; }

    /// <summary>
    /// A session that bound a client and then lost it is a live shell whose REPL has exited, so its
    /// runners target a prompt that is no longer there.
    /// </summary>
    public bool HasStaleRunners => HasConnected && ConnectionId is null;

    /// <summary>
    /// Raised when the run state changes, so the owning service can broadcast it.
    /// </summary>
    public event EventHandler<ConsoleSessionRunState>? StateChanged;

    /// <summary>
    /// The single-flight start, awaited by attach so a view never observes a half-started session.
    /// </summary>
    public Task? StartTask { get; set; }

    /// <summary>
    /// The currently attached view, or null. Read by the host to carry an attachment across a reopen.
    /// </summary>
    public IConsoleView? CurrentView
    {
        get
        {
            lock (_streamLock)
            {
                return _attachedView;
            }
        }
    }

    // The output so far is shell-startup noise, so an attaching view should still cover the terminal.
    // Caller holds _streamLock.
    private bool StartupPending => _markerScanner is not null && !_markerSeen && !_markerRevealed;

    public void Rekey(ResourceKey newResource)
    {
        Resource = newResource;
    }

    public async Task StartAsync(int cols, int rows, int rpcPort)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var readResult = await fileSystem.ReadAllTextAsync(Resource);
        if (readResult.IsFailure)
        {
            Fail($"Cannot read the console file: {readResult.FirstErrorMessage}");
            return;
        }
        var tomlText = readResult.Value;

        var parseResult = ConsoleDocumentConfigParser.Parse(tomlText);
        if (parseResult.IsFailure)
        {
            Fail(parseResult.FirstErrorMessage);
            return;
        }
        var config = parseResult.Value;

        if (config.UnknownFields.Count > 0)
        {
            // Advisory: the session still launches with the keys the host does define.
            _logger.LogWarning(
                $"Console document declares keys the host does not define ({string.Join(", ", config.UnknownFields)}): {Resource}");
        }

        var provider = ResolveProvider(config.Type);
        if (provider is null)
        {
            Fail($"Unknown console session type '{config.Type}'.");
            return;
        }

        // The identity and runners a Run target is resolved from, available from launch so a console is
        // targetable before any view attaches. A fresh token per launch stops a stale client from a
        // previous launch binding to this one.
        SessionId = Guid.NewGuid();
        TypeId = config.Type;
        Runners = ConsoleRunTargets.ResolveEffectiveRunners(config.Runners, provider.BuiltInRunners, config.DisabledBuiltInRunners);
        Triggers = ResolveTriggers(config);
        ConnectionId = null;
        HasConnected = false;

        // Seed the host-connection variables for every console: the shared listener port every peer dials,
        // and this console's session token, inherited by anything the shell launches.
        var environment = new Dictionary<string, string>(config.Environment);
        environment[ConsoleEnvironmentVariables.RpcPort] = rpcPort.ToString();
        environment[ConsoleEnvironmentVariables.SessionToken] = SessionId.ToString();

        var projectFolderPath = registry.ProjectFolderPath;

        var sessionContext = new ConsoleSessionContext(
            Resource,
            config.Type,
            config.Executable,
            config.Arguments,
            config.WorkingDirectory,
            environment,
            projectFolderPath,
            config.Dependencies,
            config.PythonVersion,
            config.StartupScript);

        // A provider throw (e.g. file IO while resolving a Python launch) must surface as a failed
        // session, not an unhandled exception on the open path.
        Result<ConsoleStartupInvocation> invocationResult;
        try
        {
            invocationResult = await provider.BuildStartupInvocationAsync(sessionContext);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to build the console startup invocation");
            invocationResult = Result<ConsoleStartupInvocation>.Fail(exception.Message);
        }

        if (invocationResult.IsFailure)
        {
            Fail(invocationResult.FirstErrorMessage);
            return;
        }

        var startupInvocation = invocationResult.Value;

        // Every session runs the platform shell; the session type only decides what is injected into it.
        // The injected line clears the shell-startup noise and emits the ready marker, so the buffer
        // begins on a clean screen.
        var shell = ConsoleShell.Resolve();
        var shellCommandLine = new CommandLineBuilder(shell.Executable).ToString();

        var workingDirectory = ConsoleWorkingFolder.Resolve(config.WorkingDirectory, projectFolderPath);

        var composedStartup = ShellCommandComposer.Compose(
            shell.Family,
            startupInvocation,
            workingDirectory: workingDirectory);
        var injectedCommandLine = composedStartup.Line;
        var hasInjectedLine = !string.IsNullOrEmpty(injectedCommandLine);

        var injectedLines = new List<string>();
        if (hasInjectedLine)
        {
            injectedLines.Add(injectedCommandLine);
        }
        if (!startupInvocation.HandlesStartupScript)
        {
            injectedLines.AddRange(ConsoleStartupScript.SplitLines(config.StartupScript));
        }

        var terminal = _serviceProvider.GetRequiredService<ITerminal>();
        terminal.OutputReceived += OnTerminalOutput;
        terminal.ProcessExited += OnTerminalProcessExited;
        terminal.SetSize(cols, rows);

        var environmentCopy = new Dictionary<string, string>(environment);

        // The startup invocation's own environment (e.g. the python launch defaults a retyped celbridge-py
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
        // injected lines. The marker scanner keeps the buffer clean of the shell-startup noise.
        _startupInjectionPending = injectedLines.Count > 0;
        _markerPersistsOnScreen = composedStartup.MarkerPersistsOnScreen;
        _markerSeen = false;
        _markerRevealed = false;
        if (composedStartup.ScanMarker is not null)
        {
            _markerScanner = new StartupMarkerScanner(composedStartup.ScanMarker);
        }

        try
        {
            terminal.Start(shellCommandLine, workingDirectory, environmentCopy);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start the console session");
            _startupInjectionPending = false;
            _markerScanner = null;
            terminal.OutputReceived -= OnTerminalOutput;
            terminal.ProcessExited -= OnTerminalProcessExited;
            terminal.Dispose();
            Fail(exception.Message);
            return;
        }

        _terminal = terminal;
        LaunchedConfigToml = tomlText;
        SetState(ConsoleSessionRunState.Running);

        if (injectedLines.Count > 0)
        {
            // A plain shell's reveal is held until the terminal has a real size, which arrives on the
            // first resize (at attach). Injecting at the headless guess would draw the revealed prompt at
            // the wrong width, and zsh's PROMPT_SP fill would leave a stray marker glyph when the view
            // renders at its own width. A command console injects immediately: its own output takes over
            // after the marker, so no shell prompt is drawn at the guessed width.
            var deferUntilSized = string.IsNullOrWhiteSpace(startupInvocation.Executable) &&
                composedStartup.ScanMarker is not null;

            if (deferUntilSized)
            {
                lock (_gateLock)
                {
                    _deferredInjectionLines = injectedLines;
                }
            }
            else
            {
                _startupInjector = StartupInjector.Begin(terminal, injectedLines, CompleteStartup);
            }
        }

        // Track the child so the workspace-scoped owner tears it down on project close or app crash.
        if (terminal.ProcessId is int processId)
        {
            _trackedProcessId = processId;
            _workspaceWrapper.WorkspaceService.ConsoleService.ProcessOwner.Track(processId);
        }
    }

    public ConsoleAttachSnapshot Attach(IConsoleView attachedView)
    {
        lock (_streamLock)
        {
            _attachedView = attachedView;

            return new ConsoleAttachSnapshot(
                State,
                Error,
                StartupPending,
                _outputBuffer.Snapshot(),
                LaunchedConfigToml);
        }
    }

    public void Detach(IConsoleView attachedView)
    {
        lock (_streamLock)
        {
            if (ReferenceEquals(_attachedView, attachedView))
            {
                _attachedView = null;
            }
        }
    }

    public void Input(string data)
    {
        if (_startupInjectionPending)
        {
            return;
        }

        _terminal?.Write(data);
    }

    public void Resize(int cols, int rows)
    {
        // A view reports no size until a layout pass has arranged it. Applying an empty size collapses the
        // pty to a single row and loses the output already on its screen to the reflow.
        if (cols > 0 &&
            rows > 0)
        {
            _terminal?.SetSize(cols, rows);
        }

        // A deferred reveal waits for this first size, so the revealed prompt is drawn at the width it will
        // be shown at.
        List<string>? deferredInjectionLines;
        lock (_gateLock)
        {
            deferredInjectionLines = _deferredInjectionLines;
            _deferredInjectionLines = null;
        }

        if (deferredInjectionLines is not null &&
            _terminal is not null)
        {
            _startupInjector = StartupInjector.Begin(_terminal, deferredInjectionLines, CompleteStartup);
        }
    }

    public void InjectInvocation(string invocation)
    {
        // Clear any partial input (Ctrl+U, U+0015) before submitting, so the invocation is not concatenated
        // with whatever the user had half-typed at the prompt.
        var text = "\u0015" + invocation;

        // A programmatic injection during startup queues behind the startup lines rather than racing
        // them, so a Run issued at console open still lands as type-ahead for the starting REPL.
        lock (_gateLock)
        {
            if (_startupInjectionPending)
            {
                _bufferedInjections.Add(text);
                return;
            }
        }

        _ = SubmitAsync(text);
    }

    // The submit key goes in a write of its own, a beat after the text. A terminal app that groups a
    // burst of stdin into a single paste reads a carriage return inside that burst as a literal newline
    // rather than a submit, leaving the invocation unsent at its prompt. Typing by hand does not hit this
    // because each keystroke arrives as its own write. A shell or REPL is unaffected either way.
    private async Task SubmitAsync(string text)
    {
        try
        {
            _terminal?.Write(text);

            await Task.Delay(SubmitKeyDelayMs);

            if (_disposed)
            {
                return;
            }

            _terminal?.Write(SubmitKey);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to submit an invocation to the console session");
        }
    }

    // Ends the startup phase, on the injector's worker once the startup lines have been written: the input
    // gate reopens, buffered programmatic injections flush behind them, and the marker scan is put on a
    // clock so a marker that never arrives cannot suppress output forever.
    private void CompleteStartup()
    {
        List<string> bufferedInjections;
        lock (_gateLock)
        {
            _startupInjectionPending = false;
            bufferedInjections = new List<string>(_bufferedInjections);
            _bufferedInjections.Clear();
        }

        if (bufferedInjections.Count > 0)
        {
            _ = SubmitBufferedAsync(bufferedInjections);
        }

        lock (_streamLock)
        {
            ArmMarkerSilenceTimer();
        }
    }

    // Restarts the silence window. Called on every chunk of output, so a launch that is slow but still
    // printing is never cut short. Caller holds _streamLock.
    private void ArmMarkerSilenceTimer()
    {
        if (_markerScanner is null ||
            _markerSeen ||
            _markerRevealed)
        {
            return;
        }

        if (_markerTimeout is null)
        {
            _markerTimeout = new Timer(_ => RevealStartupOutput(), null, MarkerSilenceTimeoutMs, Timeout.Infinite);
            return;
        }

        _markerTimeout.Change(MarkerSilenceTimeoutMs, Timeout.Infinite);
    }

    // Caller holds _streamLock.
    private void StopMarkerSilenceTimer()
    {
        _markerTimeout?.Dispose();
        _markerTimeout = null;
    }

    // Awaited in turn so buffered invocations submit in order rather than racing each other.
    private async Task SubmitBufferedAsync(IReadOnlyList<string> submissions)
    {
        foreach (var submission in submissions)
        {
            await SubmitAsync(submission);
        }
    }

    // The runtime has gone quiet without emitting its ready marker, so it is either far slower than a
    // console usually is or stuck. Show what it is printing rather than holding the terminal blank, but
    // keep scanning: a marker that arrives late is then still swallowed instead of printed as output.
    private void RevealStartupOutput()
    {
        IConsoleView? attachedView;
        lock (_streamLock)
        {
            // The marker can land while this callback is waiting on the lock.
            if (_markerScanner is null ||
                _markerSeen ||
                _markerRevealed)
            {
                return;
            }

            _markerRevealed = true;
            attachedView = _attachedView;
        }

        _logger.LogWarning(
            "Console '{Resource}' produced no ready marker within {TimeoutMs}ms of silence; revealing its startup output",
            Resource,
            MarkerSilenceTimeoutMs);

        attachedView?.OnStartupComplete();
    }

    // The process has exited, so the scan has no more stream to watch and what it held back is released.
    private void ReleaseMarkerScan()
    {
        IConsoleView? attachedView;
        string held;
        bool completeStartup;
        lock (_streamLock)
        {
            if (_markerScanner is null)
            {
                return;
            }

            held = _markerScanner.Flush();
            _markerScanner = null;
            attachedView = _attachedView;

            // A session whose marker arrived has already revealed.
            completeStartup = !_markerSeen;

            if (held.Length > 0)
            {
                _outputBuffer.Append(held);
            }
        }

        if (held.Length > 0)
        {
            attachedView?.OnOutput(held);
        }

        if (completeStartup)
        {
            attachedView?.OnStartupComplete();
        }
    }

    private void LogDiagnostics(IReadOnlyList<string> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _logger.LogInformation("Console '{Resource}' reported: {Diagnostic}", Resource, diagnostic);
        }
    }

    // A session that ends mid-sequence leaves the scanner holding a partial match, which is ordinary
    // output rather than a diagnostic once no more is coming.
    private void FlushHeldDiagnosticText()
    {
        IConsoleView? attachedView;
        string held;
        lock (_streamLock)
        {
            held = _diagnosticScanner.Flush();
            attachedView = _attachedView;

            if (held.Length > 0)
            {
                _outputBuffer.Append(held);
            }
        }

        if (held.Length > 0)
        {
            attachedView?.OnOutput(held);
        }
    }

    private void OnTerminalOutput(object? sender, string output)
    {
        if (_disposed)
        {
            return;
        }

        IConsoleView? attachedView;
        string forwarded;
        IReadOnlyList<string> diagnostics;
        var startupCompleted = false;
        var markerPending = false;

        lock (_streamLock)
        {
            // Diagnostics come off the raw stream, ahead of the marker scan. A pty forwards an OSC as it
            // parses it rather than when it paints the surrounding text, so a diagnostic can overtake the
            // ready marker, and pre-marker output is discarded. The scanner stays live for the session,
            // so a command the user runs later reports the same way.
            var scanned = _diagnosticScanner.Push(output);
            diagnostics = scanned.Diagnostics;
            forwarded = scanned.Text;

            if (_markerScanner is not null)
            {
                // Pre-marker output is shell-startup noise, neither buffered nor forwarded.
                var (text, found) = _markerScanner.Push(forwarded);
                markerPending = !found && StartupPending;

                if (found && !_markerSeen)
                {
                    _markerSeen = true;
                    startupCompleted = !_markerRevealed;
                    StopMarkerSilenceTimer();

                    // A marker that stays in the terminal's screen buffer comes back on every reflow of the
                    // rows it sits on, so the scan runs on to strip those later copies.
                    if (!_markerPersistsOnScreen)
                    {
                        _markerScanner = null;
                    }
                }
                forwarded = text;

                // Output means the launch is still progressing, so the silence window starts again.
                ArmMarkerSilenceTimer();
            }

            if (!markerPending
                && forwarded.Length > 0)
            {
                _outputBuffer.Append(forwarded);
            }
            attachedView = _attachedView;
        }

        LogDiagnostics(diagnostics);

        if (markerPending)
        {
            return;
        }

        if (startupCompleted)
        {
            attachedView?.OnStartupComplete();
        }
        if (forwarded.Length > 0)
        {
            attachedView?.OnOutput(forwarded);
        }
    }

    private void OnTerminalProcessExited(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // A marker that never arrived must not swallow the session's final output.
        ReleaseMarkerScan();
        FlushHeldDiagnosticText();

        SetState(ConsoleSessionRunState.Ended);

        IConsoleView? attachedView;
        lock (_streamLock)
        {
            attachedView = _attachedView;
        }
        attachedView?.OnSessionEnded();
    }

    private void Fail(string? message)
    {
        Error = message;
        SetState(ConsoleSessionRunState.Failed);

        // A session starts when its document opens, which may be long before anyone looks at the tab, so
        // the log is the only place the failure surfaces until then.
        _logger.LogWarning("Console session '{Resource}' failed to start: {Error}", Resource, message);
    }

    private void SetState(ConsoleSessionRunState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
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

    // Patterns are compiled once per launch rather than per resource change, since every change is tested
    // against every trigger of every open console.
    private IReadOnlyList<ConsoleTrigger> ResolveTriggers(ConsoleDocumentConfig config)
    {
        var triggers = new List<ConsoleTrigger>();
        foreach (var trigger in config.Triggers)
        {
            try
            {
                var matcher = ResourcePathMatcher.Compile(trigger.Pattern);
                triggers.Add(new ConsoleTrigger(matcher, trigger.Command));
            }
            catch (Exception exception)
            {
                // One unusable pattern drops its own trigger; the rest of the console still launches.
                _logger.LogWarning(exception, "Ignoring console trigger with an invalid pattern '{Pattern}'", trigger.Pattern);
            }
        }

        return triggers;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _markerTimeout?.Dispose();
        _markerTimeout = null;

        _startupInjector?.Dispose();
        _startupInjector = null;

        lock (_gateLock)
        {
            _startupInjectionPending = false;
            _bufferedInjections.Clear();
            _deferredInjectionLines = null;
        }

        lock (_streamLock)
        {
            _markerScanner = null;
            _attachedView = null;
        }

        var terminal = _terminal;
        if (terminal is not null)
        {
            _terminal = null;
            terminal.OutputReceived -= OnTerminalOutput;
            terminal.ProcessExited -= OnTerminalProcessExited;
            terminal.Dispose();
        }

        if (_trackedProcessId is int processId)
        {
            _trackedProcessId = null;
            if (_workspaceWrapper.HasWorkspaceService)
            {
                _workspaceWrapper.WorkspaceService.ConsoleService.ProcessOwner.Untrack(processId);
            }
        }

    }
}
