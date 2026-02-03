using System.Collections.Concurrent;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.Documents.Services;

public class TextEditorWebViewPool
{
    private readonly ILogger<TextEditorWebViewPool> _logger;
    private readonly ConcurrentQueue<WebView2> _pool;
    private readonly HashSet<WebView2> _activeInstances;
    private readonly int _maxPoolSize;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private readonly object _lock = new object();
    private bool _isInitialized = false;
    private bool _isShuttingDown = false;

    private Task? _initializationTask = null;

    public TextEditorWebViewPool(int poolSize)
    {
        _logger = ServiceLocator.AcquireService<ILogger<TextEditorWebViewPool>>();
        _maxPoolSize = poolSize;
        _pool = new ConcurrentQueue<WebView2>();
        _activeInstances = new HashSet<WebView2>();

#if WINDOWS
        // Start initialization but don't await it.
        // This allows the WebView pool to be populated in the background.
        _initializationTask = InitializePoolAsync();
#endif
    }

    private async Task InitializePoolAsync()
    {
        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized || _isShuttingDown)
                return;

            for (int i = 0; i < _maxPoolSize; i++)
            {
                try
                {
                    var webView = await CreateTextEditorWebView();
                    _pool.Enqueue(webView);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to create WebView2 instance {i + 1} of {_maxPoolSize} during pool initialization");
                }
            }

            _isInitialized = true;
            _logger.LogDebug($"TextEditorWebViewPool initialized with {_pool.Count} of {_maxPoolSize} instances");
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    public async Task<WebView2> AcquireInstance()
    {
        WebView2? webView = null;
        bool needsCreation = false;

        lock (_lock)
        {
            if (_isShuttingDown)
            {
                throw new InvalidOperationException("Cannot acquire WebView2 instances during shutdown");
            }

            // Try to get an instance from the pool first (don't wait for initialization)
            if (_pool.TryDequeue(out webView))
            {
                _activeInstances.Add(webView);
            }
            else
            {
                needsCreation = true;
            }
        }

        // Create outside the lock if needed
        if (needsCreation)
        {
            webView = await CreateTextEditorWebView();

            lock (_lock)
            {
                if (_isShuttingDown)
                {
                    CloseWebView(webView);
                    throw new InvalidOperationException("Cannot acquire WebView2 instances during shutdown");
                }
                _activeInstances.Add(webView);
            }
        }

        Guard.IsNotNull(webView);
        return webView;
    }

    public async Task ReleaseInstanceAsync(WebView2 webView)
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
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create replacement WebView2 instance for pool during release");
            }
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

        _initializationSemaphore.Dispose();
        _logger.LogDebug("TextEditorWebViewPool shutdown complete");
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

        // Inject centralized keyboard shortcut handler for F11 and other global shortcuts
        await WebView2Helper.InjectKeyboardShortcutHandlerAsync(webView.CoreWebView2);

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
