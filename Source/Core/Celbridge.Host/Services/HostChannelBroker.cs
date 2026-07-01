using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Celbridge.Host;

/// <summary>
/// Default IHostChannelBroker. Holds a map of unguessable, view-scoped tokens to the proxy channels
/// awaiting their page's WebSocket connection. An entry is removed when the view disposes the proxy
/// channel; the token is not consumed on bind, so a reloaded page can reconnect to the same channel.
/// </summary>
public sealed class HostChannelBroker : IHostChannelBroker
{
    private readonly ConcurrentDictionary<string, ProxyHostChannel> _pendingConnections = new();

    public PendingHostConnection CreatePendingConnection()
    {
        // Used as a connection credential, so mint it from a cryptographic RNG rather than Guid.NewGuid,
        // whose randomness is not contractually guaranteed across platforms.
        var token = RandomNumberGenerator.GetHexString(32, lowercase: true);
        var channel = new ProxyHostChannel(() => _pendingConnections.TryRemove(token, out _));
        _pendingConnections[token] = channel;

        return new PendingHostConnection(channel, token);
    }

    public bool TryBindConnection(string token, IHostChannel socketChannel)
    {
        // The token is kept (not consumed on bind) so a synthetic-origin editor whose page is reloaded
        // (e.g. a WebView reattach) can reconnect and re-bind to the same proxy channel. The entry is
        // removed when the view disposes the proxy channel.
        if (string.IsNullOrEmpty(token)
            || !_pendingConnections.TryGetValue(token, out var proxyChannel))
        {
            return false;
        }

        return proxyChannel.Bind(socketChannel);
    }
}
