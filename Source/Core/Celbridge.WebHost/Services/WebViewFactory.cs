using Celbridge.Logging;
using Celbridge.Settings;

namespace Celbridge.WebHost.Services;

internal class WebViewFactory : IWebViewFactory, IDisposable
{
    // How many views are kept ready ahead of time.
    private const int PrewarmCount = 3;

    private readonly ILogger<WebViewFactory> _logger;
    private readonly IWebViewPlatform _webViewPlatform;
    private readonly IWebSurfaceMessageDispatcher _messageDispatcher;
    private readonly IFeatureFlags _featureFlags;

    // Views that have been created but not handed out. A view never comes back once its owner has it.
    private readonly Queue<WebViewBase> _prewarmQueue = new();
    private readonly object _lock = new();
    private bool _isShuttingDown = false;

    private Task? _prewarmTask = null;

    public WebViewFactory(
        ILogger<WebViewFactory> logger,
        IWebViewPlatform webViewPlatform,
        IWebSurfaceMessageDispatcher messageDispatcher,
        IFeatureFlags featureFlags)
    {
        _logger = logger;
        _webViewPlatform = webViewPlatform;
        _messageDispatcher = messageDispatcher;
        _featureFlags = featureFlags;

        // Start prewarming but don't await it. The queue fills in the background.
        _prewarmTask = PrewarmAsync();
    }

    private async Task PrewarmAsync()
    {
        for (int i = 0; i < PrewarmCount; i++)
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
                var webView = await _webViewPlatform.CreateWebViewAsync();

                lock (_lock)
                {
                    if (_isShuttingDown)
                    {
                        DisposeWebView(webView);
                        return;
                    }
                    _prewarmQueue.Enqueue(webView);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to prewarm web view {i + 1} of {PrewarmCount}");
            }
        }

        int prewarmedCount;
        lock (_lock)
        {
            prewarmedCount = _prewarmQueue.Count;
        }

        _logger.LogDebug($"WebViewFactory prewarmed {prewarmedCount} of {PrewarmCount} web views");
    }

    public async Task<IEditorWebView> AcquireAsync(WebViewOptions options)
    {
        WebViewBase? webView = null;
        bool needsCreation = false;
        bool shouldReplenish = false;

        lock (_lock)
        {
            if (_isShuttingDown)
            {
                throw new InvalidOperationException("Cannot acquire web views during shutdown");
            }

            // Take a prewarmed view first, without waiting for prewarming to finish.
            if (_prewarmQueue.Count > 0)
            {
                webView = _prewarmQueue.Dequeue();

                // Top the queue up as it runs low.
                if (_prewarmQueue.Count < PrewarmCount)
                {
                    shouldReplenish = true;
                }
            }
            else
            {
                needsCreation = true;

                // Replenish an empty queue too. Otherwise the queue stays empty for the rest of the session, and
                // every later acquire pays for a full WebView2 creation inline.
                shouldReplenish = true;
            }
        }

        // Creating a view is slow, so the lock is not held during the await. Other threads can reach the queue
        // meanwhile.
        if (needsCreation)
        {
            webView = await _webViewPlatform.CreateWebViewAsync();

            lock (_lock)
            {
                if (_isShuttingDown)
                {
                    DisposeWebView(webView);
                    throw new InvalidOperationException("Cannot acquire web views during shutdown");
                }
            }
        }

        // Replenish the queue in the background
        if (shouldReplenish)
        {
            _ = ReplenishPrewarmQueueAsync();
        }

        Guard.IsNotNull(webView);

        // Applied here because a prewarmed view has not navigated yet. A view that fails to configure is closed here,
        // because the caller never receives it.
        try
        {
            await webView.ConfigureAsync(options, _featureFlags);
        }
        catch
        {
            DisposeWebView(webView);
            throw;
        }

        _messageDispatcher.Observe(webView);

        return webView;
    }

    private async Task ReplenishPrewarmQueueAsync()
    {
        try
        {
            var webView = await _webViewPlatform.CreateWebViewAsync();

            lock (_lock)
            {
                if (_isShuttingDown || _prewarmQueue.Count >= PrewarmCount)
                {
                    DisposeWebView(webView);
                    return;
                }
                _prewarmQueue.Enqueue(webView);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prewarm a web view to replenish the queue");
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
        if (_prewarmTask != null && !_prewarmTask.IsCompleted)
        {
            // Prewarming is still in progress.
            // Wait briefly but don't block indefinitely
            try
            {
                var timeoutTask = Task.Delay(2000);
                var completedTask = await Task.WhenAny(_prewarmTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Prewarming did not complete within timeout. Proceeding with cleanup.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "An exception occurred during prewarming while shutting down");
            }
        }

        lock (_lock)
        {
            _isShuttingDown = true;

            // Close the views that were never handed out
            while (_prewarmQueue.Count > 0)
            {
                var webView = _prewarmQueue.Dequeue();
                DisposeWebView(webView);
            }
        }

        _logger.LogDebug("WebViewFactory shutdown complete");
    }

    // Disposes a view that its caller never received.
    private void DisposeWebView(WebViewBase? webView)
    {
        if (webView == null)
            return;

        try
        {
            webView.Dispose();
        }
        catch
        {
            // Ignore exceptions during cleanup
        }
    }
}
