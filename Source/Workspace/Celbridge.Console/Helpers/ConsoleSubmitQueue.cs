using Celbridge.Logging;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Submits text to a console's terminal one submission at a time, in the order the submissions arrive. Each
/// submission writes its text, then the submit key a beat later.
/// </summary>
public sealed class ConsoleSubmitQueue
{
    // Carriage return, the submit key at a shell or REPL prompt.
    private const string SubmitKey = "\r";

    // How long after the text the submit key is written. Long enough that a terminal app grouping a burst of stdin
    // does not take the two for one paste.
    private static readonly TimeSpan SubmitKeyDelay = TimeSpan.FromMilliseconds(100);

    private readonly Action<string> _write;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    private readonly object _lock = new();

    // The last submission queued. Each submission waits for the one before it.
    private Task _lastSubmission = Task.CompletedTask;
    private volatile bool _isStopped;

    /// <summary>
    /// Creates a queue that writes each piece of text with the write action.
    /// </summary>
    public ConsoleSubmitQueue(Action<string> write, ILogger logger, TimeProvider? timeProvider = null)
    {
        _write = write;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Adds a submission after those already queued. A submission that started while another waited to write its
    /// submit key would clear the other's text from the prompt.
    /// </summary>
    public void Enqueue(string text)
    {
        lock (_lock)
        {
            _lastSubmission = SubmitAfterAsync(_lastSubmission, text);
        }
    }

    /// <summary>
    /// Stops the queue. A submission still waiting writes nothing more.
    /// </summary>
    public void Stop()
    {
        _isStopped = true;
    }

    // The submit key goes in a separate write, a beat after the text. A terminal app can group a burst of stdin into
    // a single paste, and then reads a carriage return inside that burst as a literal newline. The invocation would
    // sit unsent at the app's prompt. Typing by hand avoids this, because each keystroke arrives as a separate write.
    // A shell or REPL submits either way.
    private async Task SubmitAfterAsync(Task previousSubmission, string text)
    {
        // A submission never faults, since each one catches its own exceptions.
        await previousSubmission;

        try
        {
            if (_isStopped)
            {
                return;
            }

            _write(text);

            await Task.Delay(SubmitKeyDelay, _timeProvider);

            if (_isStopped)
            {
                return;
            }

            _write(SubmitKey);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to submit an invocation to the console session");
        }
    }
}
