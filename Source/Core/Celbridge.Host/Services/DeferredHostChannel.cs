using Celbridge.Core;
using Celbridge.Logging;

namespace Celbridge.Host;

/// <summary>
/// IHostChannel that a view constructs synchronously, before the WebView page has opened its
/// WebSocket back to the host. Outbound messages are buffered until the real socket channel binds,
/// and inbound messages are re-raised from the bound channel once it connects.
/// </summary>
public sealed class DeferredHostChannel : IHostChannel, IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _pendingOutbound = new();
    private readonly Action _onDisposed;
    private readonly ILogger<DeferredHostChannel> _logger;
    private IHostChannel? _boundChannel;
    private bool _disposed;

    public event EventHandler<string>? MessageReceived;

    internal DeferredHostChannel(Action onDisposed)
    {
        _onDisposed = onDisposed;
        _logger = ServiceLocator.AcquireService<ILogger<DeferredHostChannel>>();
    }

    public void PostMessage(string json)
    {
        IHostChannel boundChannel;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_boundChannel is null)
            {
                _pendingOutbound.Add(json);
                return;
            }

            boundChannel = _boundChannel;
        }

        boundChannel.PostMessage(json);
    }

    /// <summary>
    /// Binds the socket channel that the page connected back over. Flushes buffered outbound messages in
    /// order and forwards all future inbound messages. Supports re-binding: if a previous connection is
    /// still bound (a reloaded page reconnecting), it is detached and disposed first. Returns false only
    /// if the view was already torn down, in which case the caller must dispose the socket channel.
    /// </summary>
    internal bool Bind(IHostChannel socketChannel)
    {
        IHostChannel? previousChannel;
        List<string> bufferedOutbound;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            previousChannel = _boundChannel;
            _boundChannel = socketChannel;
            bufferedOutbound = new List<string>(_pendingOutbound);
            _pendingOutbound.Clear();
        }

        if (previousChannel is not null)
        {
            previousChannel.MessageReceived -= OnSocketMessageReceived;
            (previousChannel as IDisposable)?.Dispose();
        }

        socketChannel.MessageReceived += OnSocketMessageReceived;

        foreach (var json in bufferedOutbound)
        {
            socketChannel.PostMessage(json);
        }

        return true;
    }

    private void OnSocketMessageReceived(object? sender, string json)
    {
        MessageReceived?.Invoke(this, json);
    }

    public void Dispose()
    {
        IHostChannel? boundChannel;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            boundChannel = _boundChannel;
            _boundChannel = null;
            _pendingOutbound.Clear();
        }

        if (boundChannel is not null)
        {
            boundChannel.MessageReceived -= OnSocketMessageReceived;
            (boundChannel as IDisposable)?.Dispose();
        }

        _onDisposed();
    }
}
