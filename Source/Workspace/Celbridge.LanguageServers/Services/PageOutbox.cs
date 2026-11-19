namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The messages waiting for one page, in order.
/// </summary>
internal sealed class PageOutbox
{
    private readonly Lock _lock = new();
    private readonly Queue<string> _messages = new();
    private readonly SemaphoreSlim _available = new(0);
    private bool _completed;

    /// <summary>
    /// Adds a message after the messages already waiting.
    /// </summary>
    public void Enqueue(string message)
    {
        lock (_lock)
        {
            if (_completed)
            {
                return;
            }

            _messages.Enqueue(message);
        }

        _available.Release();
    }

    /// <summary>
    /// Ends the outbox. The reader gets the messages already waiting, and then null.
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
        }

        _available.Release();
    }

    /// <summary>
    /// Returns the next message, waiting for one if needed. Returns null once the outbox has ended.
    /// </summary>
    public async ValueTask<string?> ReadNextAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken);

            lock (_lock)
            {
                if (_messages.TryDequeue(out var message))
                {
                    return message;
                }

                if (_completed)
                {
                    // The signal stays set, so every later read returns null too.
                    _available.Release();
                    return null;
                }
            }
        }
    }
}
