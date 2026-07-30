namespace Celbridge.Console.Services;

/// <summary>
/// Writes a console's startup lines into a freshly-started shell pty, once the shell's output has settled.
/// </summary>
public sealed class StartupInjector : IDisposable
{
    // A Unix shell's line editor can flush pending type-ahead as it initializes, so wait for the startup
    // output to go quiet before writing. The Windows console input buffer holds type-ahead regardless.
    private const int QuietPeriodMs = 150;

    // A shell that prints nothing at all would otherwise never look settled.
    private const int CapMs = 1500;

    private const int PollIntervalMs = 25;

    private readonly ITerminal _terminal;
    private readonly IReadOnlyList<string> _commandLines;
    private readonly Action? _onInjected;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _stateLock = new();

    private long _lastOutputTick = -1;
    private bool _disposed;

    private StartupInjector(ITerminal terminal, IReadOnlyList<string> commandLines, Action? onInjected)
    {
        _terminal = terminal;
        _commandLines = commandLines;
        _onInjected = onInjected;
    }

    /// <summary>
    /// Starts watching a terminal's output and injects the command lines, in order, once the shell
    /// settles. The callback fires after they have been written; it is skipped if the injector is
    /// disposed first.
    /// </summary>
    public static StartupInjector Begin(ITerminal terminal, IReadOnlyList<string> commandLines, Action? onInjected = null)
    {
        var injector = new StartupInjector(terminal, commandLines, onInjected);
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
                if (nowTick - startTick >= CapMs)
                {
                    break;
                }

                long lastOutputTick;
                lock (_stateLock)
                {
                    lastOutputTick = _lastOutputTick;
                }

                if (lastOutputTick >= 0 &&
                    nowTick - lastOutputTick >= QuietPeriodMs)
                {
                    break;
                }

                await Task.Delay(PollIntervalMs, cancellationToken);
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
