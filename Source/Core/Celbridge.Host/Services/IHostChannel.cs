namespace Celbridge.Host;

/// <summary>
/// Bidirectional JSON message pipe between the host and a WebView page, carrying the JSON-RPC bridge.
/// Implementations are the loopback WebSocket, and the proxy channel that binds one once the page
/// connects. Tests inject a mock.
/// </summary>
public interface IHostChannel
{
    /// <summary>
    /// Posts a JSON message to the WebView.
    /// </summary>
    void PostMessage(string json);

    /// <summary>
    /// Event raised when a message is received from the WebView.
    /// </summary>
    event EventHandler<string> MessageReceived;

    /// <summary>
    /// Raised once when the channel's underlying transport is permanently gone (the WebSocket dropped,
    /// the WebView detached).
    /// </summary>
    event EventHandler? Closed;
}
