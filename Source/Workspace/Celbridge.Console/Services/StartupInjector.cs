namespace Celbridge.Console.Services;

/// <summary>
/// How long a startup injector waits for a shell's output to settle before it writes.
/// </summary>
public sealed record StartupInjectorTiming(int QuietPeriodMs, int CapMs, int PollIntervalMs)
{
    /// <summary>
    /// The intervals used against a real shell. Tests pass shorter ones so a case that can only be
    /// proven by waiting the cap out does not wait a second and a half to do it.
    /// </summary>
    public static StartupInjectorTiming Default { get; } = new(
        // A Unix shell's line editor can flush pending type-ahead as it initializes, so wait for the
        // startup output to go quiet before writing. The Windows console input buffer holds type-ahead
        // regardless.
        QuietPeriodMs: 150,
        // A shell that prints nothing at all would otherwise never look settled.
        CapMs: 1500,
        PollIntervalMs: 25);
}

/// <summary>
/// Writes a console's startup lines into a freshly-started shell pty, once the shell's output has settled.
/// </summary>
public sealed class StartupInjector : IDisposable
{
    private readonly ITerminal _terminal;
    private readonly IReadOnlyList<string> _commandLines;
    private readonly Action? _onInjected;
    private readonly StartupInjectorTiming _timing;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _stateLock = new();

    private long _lastOutputTick = -1;
    private bool _disposed;

    private StartupInjector(
        ITerminal terminal,
        IReadOnlyList<string> commandLines,
        Action? onInjected,
        StartupInjectorTiming timing)
    {
        _terminal = terminal;
        _commandLines = commandLines;
        _onInjected = onInjected;
        _timing = timing;
    }

    /// <summary>
    /// Starts watching a terminal's output and injects the command lines, in order, once the shell
    /// settles. The callback fires after they have been written; it is skipped if the injector is
    /// disposed first.
    /// </summary>
    public static StartupInjector Begin(
        ITerminal terminal,
        IReadOnlyList<string> commandLines,
        Action? onInjected = null,
        StartupInjectorTiming? timing = null)
    {
        var injector = new StartupInjector(terminal, commandLines, onInjected, timing ?? StartupInjectorTiming.Default);
        terminal.OutputReceived += injector.OnOutputReceived;
        _ = injector.RunAsync();

        return injector;
    }

    private void OnOutputReceived(object? sender, string output)
    {
        lock (_stateLock)
        {
            _lastOutputTick = Environment.TickCount64;
        }
    }

    private async Task RunAsync()
    {
        var startTick = Environment.TickCount64;
        var cancellationToken = _cancellation.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nowTick = Environment.TickCount64;
                if (nowTick - startTick >= _timing.CapMs)
                {
                    break;
                }

                long lastOutputTick;
                lock (_stateLock)
                {
                    lastOutputTick = _lastOutputTick;
                }

                if (lastOutputTick >= 0 &&
                    nowTick - lastOutputTick >= _timing.QuietPeriodMs)
                {
                    break;
                }

                await Task.Delay(_timing.PollIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            // Disposed between loop iterations.
            return;
        }

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _terminal.OutputReceived -= OnOutputReceived;

            // Only the first line meets a shell prompt. The rest queue in the pty's type-ahead buffer and
            // are read by whatever the first line left owning the prompt, typically a starting REPL.
            foreach (var commandLine in _commandLines)
            {
                _terminal.Write(commandLine + "\r");
            }
        }

        _onInjected?.Invoke();
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _terminal.OutputReceived -= OnOutputReceived;
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
