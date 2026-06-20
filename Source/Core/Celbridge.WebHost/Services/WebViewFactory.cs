using Celbridge.Logging;
using Celbridge.UserInterface;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Services;

public class WebViewFactory : IWebViewFactory, IDisposable
{
    private const int DefaultPoolSize = 3;
    private const string SharedAssetsHostName = "shared.celbridge";
    private const string SharedAssetsFolderPath = "Celbridge.WebHost/Web";

    private const string KeyboardShortcutScript = """
        (function() {
            window.addEventListener('keydown', function(event) {
                // Handle F11 for fullscreen toggle
                if (event.key === 'F11') {
                    // Prevent default browser fullscreen and stop all propagation
                    event.preventDefault();
                    event.stopPropagation();
                    event.stopImmediatePropagation();
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            jsonrpc: '2.0',
                            method: 'host/keyboardShortcut',
                            params: {
                                key: 'F11',
                                ctrlKey: event.ctrlKey,
                                shiftKey: event.shiftKey,
                                altKey: event.altKey
                            }
                        }));
                    }
                    return false;
                }
            }, true); // Use capture phase to intercept before other handlers
        })();
        """;

    private readonly ILogger<WebViewFactory> _logger;
    private readonly Queue<WebView2> _pool;
    private readonly int _maxPoolSize;
    private readonly object _lock = new();
    private bool _isShuttingDown = false;

    private Task? _initializationTask = null;

#if !WINDOWS
    // Hidden, window-rooted host used to initialize WebView2 controls on the Skia
    // desktop head, where EnsureCoreWebView2Async never completes for a control
    // that has not been parented to a window.
    private Panel? _initHost;
#endif

    public WebViewFactory()
        : this(DefaultPoolSize)
    {
    }

    public WebViewFactory(int poolSize)
    {
        _logger = ServiceLocator.AcquireService<ILogger<WebViewFactory>>();
        _maxPoolSize = poolSize;
        _pool = new Queue<WebView2>();

#if WINDOWS
        // Start initialization but don't await it.
        // This allows the WebView pool to be populated in the background.
        _initializationTask = InitializePoolAsync();
#endif
    }

    private async Task InitializePoolAsync()
    {
        for (int i = 0; i < _maxPoolSize; i++)
        {
            lock (_lock)
            {
                if (_isShuttingDown)
                {
                    return;
                }
            }

            try
            {
                var webView = await CreateWebViewAsync();

                lock (_lock)
                {
                    if (_isShuttingDown)
                    {
                        CloseWebView(webView);
                        return;
                    }
                    _pool.Enqueue(webView);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create WebView2 instance {i + 1} of {_maxPoolSize} during pool initialization");
            }
        }

        int poolCount;
        lock (_lock)
        {
            poolCount = _pool.Count;
        }

        _logger.LogDebug($"WebViewFactory initialized with {poolCount} of {_maxPoolSize} instances");
    }

    public async Task<WebView2> AcquireAsync()
    {
        WebView2? webView = null;
        bool needsCreation = false;
        bool shouldReplenish = false;

        lock (_lock)
        {
            if (_isShuttingDown)
            {
                throw new InvalidOperationException("Cannot acquire WebView2 instances during shutdown");
            }

            // Try to get an instance from the pool first (don't wait for initialization)
            if (_pool.Count > 0)
            {
                webView = _pool.Dequeue();

                // Trigger replenishment if pool is running low
                if (_pool.Count < _maxPoolSize)
                {
                    shouldReplenish = true;
                }
            }
            else
            {
                needsCreation = true;
            }
        }

        // CreateWebViewAsync is an expensive async operation, so we avoid holding the lock
        // during the await to prevent blocking other threads from accessing the pool.
        if (needsCreation)
        {
            webView = await CreateWebViewAsync();

            lock (_lock)
            {
                if (_isShuttingDown)
                {
                    CloseWebView(webView);
                    throw new InvalidOperationException("Cannot acquire WebView2 instances during shutdown");
                }
            }
        }

        // Replenish pool in the background
        if (shouldReplenish)
        {
            _ = ReplenishPoolAsync();
        }

        Guard.IsNotNull(webView);
        return webView;
    }

    private async Task ReplenishPoolAsync()
    {
        try
        {
            var webView = await CreateWebViewAsync();

            lock (_lock)
            {
                if (_isShuttingDown || _pool.Count >= _maxPoolSize)
                {
                    CloseWebView(webView);
                    return;
                }
                _pool.Enqueue(webView);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create WebView2 instance for pool replenishment");
        }
    }

    public void Shutdown()
    {
        try
        {
            // Synchronous wrapper for ShutdownAsync with timeout protection.
            // This is safe to call during Dispose() as it won't block indefinitely.        
            var shutdownTask = ShutdownAsync();
            if (!shutdownTask.Wait(TimeSpan.FromSeconds(3)))
            {
                _logger.LogWarning("Shutdown did not complete within 3 seconds. Some cleanup may be incomplete.");
            }
        }
        catch (AggregateException ex) when (ex.InnerException != null)
        {
            // Task.Wait() wraps exceptions in an AggregateException
            _logger.LogError(ex.InnerException, "An exception occurred during shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during shutdown");
        }
    }

    public void Dispose()
    {
        Shutdown();
    }

    private async Task ShutdownAsync()
    {
        if (_initializationTask != null && !_initializationTask.IsCompleted)
        {
            // Initialization is still in progress.
            // Wait briefly but don't block indefinitely
            try
            {
                var timeoutTask = Task.Delay(2000);
                var completedTask = await Task.WhenAny(_initializationTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Pool initialization did not complete within timeout. Proceeding with cleanup.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "An exception occurred during pool initialization while shutting down");
            }
        }

        lock (_lock)
        {
            _isShuttingDown = true;

            // Clean up pooled instances
            while (_pool.Count > 0)
            {
                var webView = _pool.Dequeue();
                CloseWebView(webView);
            }
        }

        _logger.LogDebug("WebViewFactory shutdown complete");
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

    private async Task<WebView2> CreateWebViewAsync()
    {
        _logger.LogDebug("DP1: CreateWebViewAsync: constructing WebView2");
        var webView = new WebView2();

        // This fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        webView.DefaultBackgroundColor = Colors.Transparent;

        _logger.LogDebug("DP1: CreateWebViewAsync: initializing CoreWebView2");
        await InitializeCoreWebView2Async(webView);
        _logger.LogDebug("DP1: CreateWebViewAsync: CoreWebView2 initialized");

        // Map shared assets (Bootstrap Icons, etc.) for all factory-created WebViews
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            SharedAssetsHostName,
            SharedAssetsFolderPath,
            CoreWebView2HostResourceAccessKind.Allow);

        // Document-start script injection (AddScriptToExecuteOnDocumentCreatedAsync) is not
        // implemented on the Uno Skia CoreWebView2. Tolerated here for DP1 validation so the
        // WebView still navigates; a real injection alternative is DP2 (the WebView seam).
        try
        {
            // Mark this as a WebView running in the Celbridge host
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            // Inject centralized keyboard shortcut handler for F11 and other global shortcuts
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyboardShortcutScript);
        }
        catch (NotImplementedException)
        {
            _logger.LogWarning("DP1: AddScriptToExecuteOnDocumentCreatedAsync not implemented on this head; skipping startup scripts");
        }

        _logger.LogDebug("DP1: CreateWebViewAsync: WebView2 fully created and configured");
        return webView;
    }

#if WINDOWS
    // The packaged WinAppSDK WebView2 initializes without being attached to the
    // visual tree, so detached controls (including the pre-warmed pool) work.
    private static async Task InitializeCoreWebView2Async(WebView2 webView)
    {
        await webView.EnsureCoreWebView2Async();
    }
#else
    // On the Uno Skia desktop head EnsureCoreWebView2Async never completes for a
    // control that is not parented to a window. Parent the control in a hidden,
    // window-rooted host for the duration of initialization, then detach it so the
    // consumer can place it in its own container with the CoreWebView2 already live.
    private async Task InitializeCoreWebView2Async(WebView2 webView)
    {
        var host = EnsureInitHost();
        host.Children.Add(webView);
        try
        {
            if (!webView.IsLoaded)
            {
                var loadedCompletionSource = new TaskCompletionSource();
                RoutedEventHandler? onLoaded = null;
                onLoaded = (sender, args) =>
                {
                    webView.Loaded -= onLoaded;
                    loadedCompletionSource.TrySetResult();
                };
                webView.Loaded += onLoaded;
                await loadedCompletionSource.Task;
            }

            await webView.EnsureCoreWebView2Async();
        }
        finally
        {
            host.Children.Remove(webView);
        }
    }

    private Panel EnsureInitHost()
    {
        if (_initHost is not null)
        {
            return _initHost;
        }

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        if (userInterfaceService.MainWindow is not Window mainWindow ||
            mainWindow.Content is not Grid rootGrid)
        {
            throw new InvalidOperationException(
                "Cannot initialize WebView2: the application root grid is not available yet");
        }

        var host = new Grid
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        rootGrid.Children.Add(host);
        _initHost = host;

        return host;
    }
#endif
}
