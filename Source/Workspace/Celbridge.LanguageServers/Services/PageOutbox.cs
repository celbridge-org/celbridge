using System.Threading.Channels;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The messages waiting for one page, in order.
/// </summary>
internal sealed class PageOutbox
{
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    /// <summary>
    /// Adds a message after the messages already waiting. A message added after the outbox ends is dropped.
    /// </summary>
    public void Enqueue(string message)
    {
        _messages.Writer.TryWrite(message);
    }

    /// <summary>
    /// Ends the outbox. The reader gets the messages already waiting, and then null.
    /// </summary>
    public void Complete()
    {
        _messages.Writer.TryComplete();
    }

    /// <summary>
    /// Returns the next message, waiting for one if needed. Returns null once the outbox has ended, and on every
    /// read after that.
    /// </summary>
    public async ValueTask<string?> ReadNextAsync(CancellationToken cancellationToken)
    {
        while (await _messages.Reader.WaitToReadAsync(cancellationToken))
        {
            if (_messages.Reader.TryRead(out var message))
            {
                return message;
            }
        }

        return null;
    }
}
