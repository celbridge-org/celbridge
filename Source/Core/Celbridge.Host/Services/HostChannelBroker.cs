using System;
using System.Collections.Concurrent;

namespace Celbridge.Host;

/// <summary>
/// Default IHostChannelBroker. Holds a map of single-use tokens to the deferred channels awaiting
/// their page's WebSocket connection. An entry is removed when its connection is bound or when the
/// view disposes the deferred channel before the page ever connects.
/// </summary>
public sealed class HostChannelBroker : IHostChannelBroker
{
    private readonly ConcurrentDictionary<string, DeferredHostChannel> _pendingConnections = new();

    public PendingHostConnection CreatePendingConnection()
    {
        var token = Guid.NewGuid().ToString("N");
        var channel = new DeferredHostChannel(() => _pendingConnections.TryRemove(token, out _));
        _pendingConnections[token] = channel;

        return new PendingHostConnection(channel, token);
    }

    public bool TryBindConnection(string token, IHostChannel socketChannel)
    {
        if (string.IsNullOrEmpty(token)
            || !_pendingConnections.TryRemove(token, out var deferredChannel))
        {
            return false;
        }

        return deferredChannel.Bind(socketChannel);
    }
}
