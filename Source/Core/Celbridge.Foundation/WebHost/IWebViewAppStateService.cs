namespace Celbridge.WebHost;

/// <summary>
/// Application-global state replicated read-only to every WebView client (theme, and later locale,
/// feature flags, ...). The host is the sole writer. Each connected WebView receives the full snapshot
/// on connect and a fresh snapshot whenever a value changes. This is the shared half of the host->view
/// state model; per-view values (e.g. a document's writability) are carried by a separate view-state
/// channel. Transient events and commands stay on the RPC bridge.
/// </summary>
public interface IWebViewAppStateService
{
    /// <summary>
    /// Sets an application-global value and broadcasts the updated snapshot to every connected WebView.
    /// </summary>
    void SetValue(string key, string value);

    /// <summary>
    /// Registers a WebView connection so it receives broadcasts, and immediately pushes the current
    /// snapshot to it (the client does not ask; it just receives). The sendSnapshot delegate pushes a
    /// snapshot to that one WebView (the caller wires it to its RPC channel). Register after the host
    /// channel is listening so the initial push is valid. Dispose the returned registration when the
    /// WebView tears down.
    /// </summary>
    IDisposable RegisterConnection(Func<IReadOnlyDictionary<string, string>, Task> sendSnapshot);
}
