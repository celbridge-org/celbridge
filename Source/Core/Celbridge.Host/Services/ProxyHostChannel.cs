using Celbridge.Core;
using Celbridge.Logging;

namespace Celbridge.Host;

/// <summary>
/// A proxy IHostChannel a view constructs synchronously, before the WebView page has opened its
/// WebSocket back to the host. It forwards to a real transport channel once one is bound: outbound
/// messages are buffered until then, and inbound messages are re-raised from the bound channel.
/// Re-binding is supported, so a fresh transport can swap in without the view rebuilding its
/// CelbridgeHost -- whether because the page reloaded, or because a live page's socket dropped (an OS
/// suspend, a network blip) and the client reconnected on the same token.
/// </summary>
public sealed class ProxyHostChannel : IHostChannel, IDisposable
{
    // Bounds the outage buffer so a page that never reconnects cannot grow it for the rest of the
    // session. Oldest messages are the most stale, so those are dropped first.
    private const int MaxPendingOutboundMessages = 1000;

    private readonly object _gate = new();
    private readonly List<string> _pendingOutbound = new();
    private readonly Action _onDisposed;
    private readonly ILogger<ProxyHostChannel> _logger;
    private IHostChannel? _boundChannel;
    private bool _wasBound;
    private bool _disposed;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler? Closed;

    /// <summary>
    /// Raised when a fresh transport replaces an earlier one (a reconnecting page). Messages sent while
    /// the previous transport was dying may have been lost in transit, so consumers should resynchronize
    /// any state that matters.
    /// </summary>
    public event EventHandler? Rebound;

    internal ProxyHostChannel(Action onDisposed)
    {
        _onDisposed = onDisposed;
        _logger = ServiceLocator.AcquireService<ILogger<ProxyHostChannel>>();
    }

    public void PostMessage(string json)
    {
        IHostChannel? boundChannel;
        bool droppedOldest = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_boundChannel is null)
            {
                if (_pendingOutbound.Count >= MaxPendingOutboundMessages)
                {
                    _pendingOutbound.RemoveAt(0);
                    droppedOldest = true;
                }

                _pendingOutbound.Add(json);
            }

            boundChannel = _boundChannel;
        }

        if (droppedOldest)
        {
            _logger.LogDebug("Pending outbound buffer is full; dropped the oldest message");
        }

        boundChannel?.PostMessage(json);
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
        bool isRebind;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            previousChannel = _boundChannel;
            _boundChannel = socketChannel;
            isRebind = _wasBound;
            _wasBound = true;
            bufferedOutbound = new List<string>(_pendingOutbound);
            _pendingOutbound.Clear();
        }

        if (previousChannel is not null)
        {
            DetachChannel(previousChannel);
            (previousChannel as IDisposable)?.Dispose();
        }

        socketChannel.MessageReceived += OnSocketMessageReceived;
        socketChannel.Closed += OnBoundChannelClosed;

        foreach (var json in bufferedOutbound)
        {
            socketChannel.PostMessage(json);
        }

        if (isRebind)
        {
            Rebound?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    private void DetachChannel(IHostChannel channel)
    {
        channel.MessageReceived -= OnSocketMessageReceived;
        channel.Closed -= OnBoundChannelClosed;
    }

    private void OnSocketMessageReceived(object? sender, string json)
    {
        MessageReceived?.Invoke(this, json);
    }

    // The bound transport dropped (its socket closed via an OS suspend or network blip). Revert to
    // buffering so outbound messages queue instead of vanishing into the dead transport, then flush when
    // the page's reconnected socket re-binds. Ignore a stale channel already replaced by a rebind.
    private void OnBoundChannelClosed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed
                || _boundChannel is null
                || !ReferenceEquals(sender, _boundChannel))
            {
                return;
            }

            DetachChannel(_boundChannel);
            _boundChannel = null;
        }

        _logger.LogDebug("Host channel transport dropped; buffering outbound until the page reconnects");
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
            DetachChannel(boundChannel);
            (boundChannel as IDisposable)?.Dispose();
        }

        _onDisposed();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
