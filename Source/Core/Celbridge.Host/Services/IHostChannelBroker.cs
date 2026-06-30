namespace Celbridge.Host;

/// <summary>
/// Brokers the connection between a view's DeferredHostChannel, created synchronously before the
/// WebView page loads, and the WebSocket the page later opens back to the host. The view registers a
/// pending connection and embeds the returned token in the page's navigation URL. The host's WebSocket
/// endpoint binds the accepted socket to the matching pending channel by that token.
/// </summary>
public interface IHostChannelBroker
{
    /// <summary>
    /// Creates a deferred channel registered under a fresh, unguessable token. The view builds its
    /// CelbridgeHost on the returned channel and embeds the token in the page's navigation URL.
    /// </summary>
    PendingHostConnection CreatePendingConnection();

    /// <summary>
    /// Binds an accepted WebSocket channel to the pending connection registered under the token.
    /// Returns false when no live pending connection matches the token (unknown, already bound, or the
    /// view was torn down before the page connected).
    /// </summary>
    bool TryBindConnection(string token, IHostChannel socketChannel);
}

/// <summary>
/// A pending host channel awaiting its WebView page's WebSocket connection. Channel is the deferred
/// channel the view builds its CelbridgeHost on. Token identifies the connection in the page's
/// navigation URL.
/// </summary>
public sealed record PendingHostConnection(DeferredHostChannel Channel, string Token);
