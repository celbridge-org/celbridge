using Celbridge.WebHost;

namespace Celbridge.Host;

/// <summary>
/// JSON-RPC method names for the application-state channel.
/// </summary>
public static class AppStateRpcMethods
{
    /// <summary>Host to client: a fresh app-state snapshot, pushed on connect and on every change.</summary>
    public const string Changed = "appState/changed";
}

/// <summary>
/// Default IWebViewAppStateService. Holds the application-global state and the set of active WebView
/// connections, and broadcasts changes to them. Values are set from UI-thread code paths, so a single
/// lock guards the shared collections.
/// </summary>
public sealed class WebViewAppStateService : IWebViewAppStateService
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
        // without asking. Callers register after StartListening, so this notification is valid; the
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
        private readonly WebViewAppStateService _service;
        private readonly Func<IReadOnlyDictionary<string, string>, Task> _sendSnapshot;
        private bool _disposed;

        public Connection(WebViewAppStateService service, Func<IReadOnlyDictionary<string, string>, Task> sendSnapshot)
        {
            _service = service;
            _sendSnapshot = sendSnapshot;
        }

        public Task PushAsync()
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            return _sendSnapshot(_service.Snapshot());
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.Remove(this);
        }
    }
}
