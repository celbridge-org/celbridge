using Celbridge.WebHost;

namespace Celbridge.Host;

/// <summary>
/// JSON-RPC method names for the host-to-client state channels. Each carries a fresh snapshot, pushed on
/// connect and on every change.
/// </summary>
public static class StateRpcMethods
{
    public const string AppStateChanged = "appState/changed";
    public const string ViewStateChanged = "viewState/changed";
}

/// <summary>
/// State store kernel: holds the values and the active WebView connections, and broadcasts the full snapshot
/// on connect and on every change. A single lock guards the shared collections (values are set from the UI
/// thread). One instance backs the app-global store. CreateViewState mints one more per document view.
/// </summary>
public sealed class StateStore : IStateStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _state = new();
    private readonly List<Connection> _connections = new();

    public void SetValue(string key, string value)
    {
        List<Connection> targets;
        lock (_lock)
        {
            _state[key] = value;
            targets = new List<Connection>(_connections);
        }

        foreach (var connection in targets)
        {
            _ = connection.PushAsync();
        }
    }

    public IDisposable RegisterConnection(Func<IReadOnlyDictionary<string, string>, Task> sendSnapshot)
    {
        var connection = new Connection(this, sendSnapshot);
        lock (_lock)
        {
            _connections.Add(connection);
        }

        // Push the current snapshot immediately so a freshly-connected WebView gets the initial state
        // without asking. Callers register after StartListening, so this notification is valid. The
        // host channel buffers it until the page's socket binds.
        _ = connection.PushAsync();

        return connection;
    }

    private Dictionary<string, string> Snapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_state);
        }
    }

    private void Remove(Connection connection)
    {
        lock (_lock)
        {
            _connections.Remove(connection);
        }
    }

    private sealed class Connection : IDisposable
    {
        private readonly StateStore _store;
        private readonly Func<IReadOnlyDictionary<string, string>, Task> _sendSnapshot;
        private bool _disposed;

        public Connection(StateStore store, Func<IReadOnlyDictionary<string, string>, Task> sendSnapshot)
        {
            _store = store;
            _sendSnapshot = sendSnapshot;
        }

        public Task PushAsync()
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            return _sendSnapshot(_store.Snapshot());
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _store.Remove(this);
        }
    }
}

/// <summary>
/// Default IWebViewStateService. Owns the single app-global store and mints a fresh store per document view.
/// </summary>
public sealed class WebViewStateService : IWebViewStateService
{
    private readonly StateStore _appState = new();

    public IStateStore AppState => _appState;

    public IStateStore CreateViewState()
    {
        return new StateStore();
    }
}
