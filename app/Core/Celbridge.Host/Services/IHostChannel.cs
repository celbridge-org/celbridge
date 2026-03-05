namespace Celbridge.Host;

/// <summary>
/// Abstraction over WebView2 message passing for testability.
/// Production code uses HostChannel which wraps CoreWebView2.
/// Tests inject a mock implementation.
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
}
