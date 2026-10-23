namespace Celbridge.Console.Services;

/// <summary>
/// Writes a console's startup lines into a freshly-started shell pty, once the shell's output has settled.
/// </summary>
public sealed class StartupInjector : IDisposable
{
    // A Unix shell's line editor can flush pending type-ahead while the shell starts, so the injector waits for the
    // startup output to go quiet before writing. The Windows console input buffer keeps type-ahead either way.
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(150);

    // The longest wait, for a shell that prints no startup output.
    private static readonly TimeSpan Cap = TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly ITerminal _terminal;
    private readonly IReadOnlyList<string> _commandLines;
    private readonly Action? _onInjected;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _stateLock = new();

    private long _lastOutputTimestamp = -1;
    private bool _disposed;

    private StartupInjector(
        ITerminal terminal,
        IReadOnlyList<string> commandLines,
        Action? onInjected,
        TimeProvider timeProvider)
    {
        _terminal = terminal;
        _commandLines = commandLines;
        _onInjected = onInjected;
        _timeProvider = timeProvider;
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
        TimeProvider? timeProvider = null)
    {
        var injector = new StartupInjector(terminal, commandLines, onInjected, timeProvider ?? TimeProvider.System);
        terminal.OutputReceived += injector.OnOutputReceived;
        _ = injector.RunAsync();

        return injector;
    }

    private void OnOutputReceived(object? sender, string output)
    {
        lock (_stateLock)
        {
            _lastOutputTimestamp = _timeProvider.GetTimestamp();
        }
    }

    private async Task RunAsync()
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        var cancellationToken = _cancellation.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_timeProvider.GetElapsedTime(startTimestamp) >= Cap)
                {
                    break;
                }

                long lastOutputTimestamp;
                lock (_stateLock)
                {
                    lastOutputTimestamp = _lastOutputTimestamp;
                }

                if (lastOutputTimestamp >= 0 &&
                    _timeProvider.GetElapsedTime(lastOutputTimestamp) >= QuietPeriod)
                {
                    break;
                }

                await Task.Delay(PollInterval, _timeProvider, cancellationToken);
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
