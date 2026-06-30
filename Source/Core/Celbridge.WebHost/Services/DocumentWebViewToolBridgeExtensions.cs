using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Registers a WebView2 with the tool bridge, marshalling its eval, reload, and
/// screenshot calls onto the UI thread before delegating the per-platform work to
/// the WebView adapter.
/// </summary>
public static class DocumentWebViewToolBridgeExtensions
{
    // ~2 frames at 60fps. Content-ready can fire before the first paint commits,
    // so this gives every capture a minimum of one frame of paint headroom.
    private const int PaintBackstopMs = 50;

    /// <summary>
    /// Registers a WebView2 with the tool bridge. Must be called from the UI
    /// thread. The current DispatcherQueue is captured for the registration's
    /// lifetime.
    /// </summary>
    public static void RegisterWebView2(
        this IDocumentWebViewToolBridge bridge,
        ResourceKey resource,
        WebView2 webView,
        IWebViewAdapter webViewAdapter)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "RegisterWebView2 must be called from a thread with a DispatcherQueue (typically the UI thread).");

        var coreWebView2 = webView.CoreWebView2;

        bridge.Register(
            resource,
            expression => DispatchEvalAsync(coreWebView2, dispatcherQueue, expression, webViewAdapter),
            clearCache => DispatchReloadAsync(coreWebView2, dispatcherQueue, clearCache, webViewAdapter),
            request => DispatchScreenshotAsync(webView, dispatcherQueue, request, webViewAdapter));
    }

    private static Task<string> DispatchEvalAsync(
        CoreWebView2 coreWebView2,
        DispatcherQueue dispatcherQueue,
        string expression,
        IWebViewAdapter webViewAdapter)
    {
        var tcs = new TaskCompletionSource<string>();

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await webViewAdapter.EvalAsync(coreWebView2, expression);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new InvalidOperationException("Failed to dispatch eval to the UI thread"));
        }

        return tcs.Task;
    }

    private static Task DispatchReloadAsync(
        CoreWebView2 coreWebView2,
        DispatcherQueue dispatcherQueue,
        bool clearCache,
        IWebViewAdapter webViewAdapter)
    {
        var tcs = new TaskCompletionSource();

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await webViewAdapter.ReloadAsync(coreWebView2, clearCache);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new InvalidOperationException("Failed to dispatch reload to the UI thread"));
        }

        return tcs.Task;
    }

    private static Task<ScreenshotData> DispatchScreenshotAsync(
        WebView2 webView,
        DispatcherQueue dispatcherQueue,
        ScreenshotRequest request,
        IWebViewAdapter webViewAdapter)
    {
        var tcs = new TaskCompletionSource<ScreenshotData>();

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                // Inactive tabs pause the renderer, so the capture would hang. Fail
                // fast both before and after the settle delay.
                if (!IsRenderableNow(webView))
                {
                    throw new InvalidOperationException(
                        "Screenshot requires the target document to be the active tab. " +
                        "WebView2 pauses rendering for inactive tabs, so the screenshot cannot " +
                        "complete. Open the document with document_open and ensure its tab is " +
                        "selected before calling webview_screenshot.");
                }

                var totalSettleMs = PaintBackstopMs + request.SettleMs;
                if (totalSettleMs > 0)
                {
                    await Task.Delay(totalSettleMs);
                }

                if (!IsRenderableNow(webView))
                {
                    throw new InvalidOperationException(
                        "Screenshot target became inactive during the settle delay. " +
                        "Re-activate the document tab and retry.");
                }

                var data = await webViewAdapter.CaptureScreenshotAsync(webView, request);
                tcs.TrySetResult(data);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new InvalidOperationException("Failed to dispatch screenshot to the UI thread"));
        }

        return tcs.Task;
    }

    // True when the WebView2 is parented in the visual tree and visible. TabView
    // unloads inactive tabs, so IsLoaded going false is the signal that the
    // renderer has paused and a screenshot cannot complete.
    private static bool IsRenderableNow(WebView2 webView)
    {
        return webView.IsLoaded
            && webView.Visibility == Microsoft.UI.Xaml.Visibility.Visible
            && webView.ActualWidth > 0
            && webView.ActualHeight > 0;
    }
}
