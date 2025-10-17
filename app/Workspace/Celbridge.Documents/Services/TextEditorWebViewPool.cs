using Celbridge.UserInterface;
using Microsoft.Web.WebView2.Core;
using System.Collections.Concurrent;
using Windows.Foundation;

namespace Celbridge.Documents.Services;

public class TextEditorWebViewPool
{
    private readonly ConcurrentQueue<WebView2> _pool;
    private readonly HashSet<WebView2> _activeInstances;
    private readonly int _maxPoolSize;
    private readonly object _lock = new object();
    private bool _isShuttingDown = false;

    public TextEditorWebViewPool(int poolSize)
    {
        _maxPoolSize = poolSize;
        _pool = new ConcurrentQueue<WebView2>();
        _activeInstances = new HashSet<WebView2>();

#if WINDOWS
        InitializePool();
#endif
    }

    private async void InitializePool()
    {
        for (int i = 0; i < _maxPoolSize; i++)
        {
            var webView = await CreateTextEditorWebView();
            _pool.Enqueue(webView);
        }
    }

    public async Task<WebView2> AcquireInstance()
    {
        WebView2? webView;
        
        lock (_lock)
        {
            if (_isShuttingDown)
            {
                throw new InvalidOperationException("Cannot acquire WebView2 instances during shutdown");
            }

            if (!_pool.TryDequeue(out webView))
            {
                // Pool is empty, we'll create a new instance outside the lock
            }
            else
            {
                _activeInstances.Add(webView);
                return webView;
            }
        }

        // Create a new instance if the pool was empty
        webView = await CreateTextEditorWebView();
        
        lock (_lock)
        {
            if (!_isShuttingDown)
            {
                _activeInstances.Add(webView);
            }
        }

        Guard.IsNotNull(webView);
        return webView;
    }

    public async void ReleaseInstance(WebView2 webView)
    {
        bool shouldCreateNew = false;

        lock (_lock)
        {
            // Remove from active instances
            _activeInstances.Remove(webView);

            // If we're shutting down, just close it and don't refill the pool
            if (_isShuttingDown)
            {
                CloseWebView(webView);
                return;
            }

            // Check if we should create a new one for the pool
            if (_pool.Count < _maxPoolSize)
            {
                shouldCreateNew = true;
            }
        }

        // Close the old WebView2 instance to free resources
        CloseWebView(webView);

        // Create a fresh instance for the pool to ensure the Monaco editor starts in a pristine state
        if (shouldCreateNew)
        {
            var newWebView = await CreateTextEditorWebView();
            
            lock (_lock)
            {
                if (!_isShuttingDown)
                {
                    _pool.Enqueue(newWebView);
                }
                else
                {
                    // If we're shutting down by the time we created this, close it immediately
                    CloseWebView(newWebView);
                }
            }
        }
    }

    public void Shutdown()
    {
        lock (_lock)
        {
            _isShuttingDown = true;

            // Clean up pooled instances
            while (_pool.TryDequeue(out var webView))
            {
                CloseWebView(webView);
            }

            // Clean up active instances
            foreach (var webView in _activeInstances)
            {
                CloseWebView(webView);
            }
            _activeInstances.Clear();
        }
    }

    private static void CloseWebView(WebView2? webView)
    {
        if (webView == null)
            return;

        try
        {
            webView.Close();
        }
        catch
        {
            // Ignore exceptions during cleanup
        }
    }

    private static async Task<WebView2> CreateTextEditorWebView()
    {
        var webView = new WebView2();

        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        webView.DefaultBackgroundColor = Colors.Transparent;

        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "MonacoEditor",
            "Celbridge.Documents/Web/Monaco",
            CoreWebView2HostResourceAccessKind.Allow);

        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

        // Set Monaco color theme to match the user interface theme        
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var theme = userInterfaceService.UserInterfaceTheme;
        var vsTheme = theme == UserInterfaceTheme.Light ? "vs-light" : "vs-dark";
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.theme = '{vsTheme}';");

        webView.CoreWebView2.Navigate("http://MonacoEditor/index.html");

        bool isEditorReady = false;
        TypedEventHandler<WebView2, CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (sender, e) =>
        {
            var message = e.TryGetWebMessageAsString();

            if (message == "editor_ready")
            {
                isEditorReady = true;
                return;
            }

            throw new InvalidOperationException($"Expected 'editor_ready' message, but received: {message}");
        };

        webView.WebMessageReceived += onWebMessageReceived;

        while (!isEditorReady)
        {
            await Task.Delay(50);
        }

        webView.WebMessageReceived -= onWebMessageReceived;

        return webView;
    }
}
