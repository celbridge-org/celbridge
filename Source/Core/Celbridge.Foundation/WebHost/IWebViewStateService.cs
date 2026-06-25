namespace Celbridge.WebHost;

/// <summary>
/// A host-owned store of string values, mirrored read-only to WebView clients: the host is the sole writer,
/// and every registered connection receives the full snapshot on connect and on each change. Used at two
/// scopes through one primitive: one app-global instance and one per document view.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Sets a value and broadcasts the updated snapshot to every registered connection.
    /// </summary>
    void SetValue(string key, string value);

    /// <summary>
    /// Registers a WebView connection (via a delegate that pushes a snapshot over its RPC channel) and
    /// immediately pushes the current snapshot to it. Register after the host channel is listening; dispose
    /// the registration on teardown.
    /// </summary>
    IDisposable RegisterConnection(Func<IReadOnlyDictionary<string, string>, Task> sendSnapshot);
}

/// <summary>
/// Brokers the WebView state stores: AppState is the app-global store shared by every WebView; CreateViewState
/// mints a fresh store per document view.
/// </summary>
public interface IWebViewStateService
{
    /// <summary>The app-global state store, shared by every WebView.</summary>
    IStateStore AppState { get; }

    /// <summary>Creates a fresh per-view state store.</summary>
    IStateStore CreateViewState();
}
