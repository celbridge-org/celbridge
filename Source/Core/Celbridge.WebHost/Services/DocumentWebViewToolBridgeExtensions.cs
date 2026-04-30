using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// WebView2 platform helpers for IDocumentWebViewToolBridge. Marshals eval,
/// reload, and screenshot calls onto the UI thread.
/// </summary>
public static class DocumentWebViewToolBridgeExtensions
{
    // Bounds the wait for Page.captureScreenshot. Inactive WinUI tabs pause the
    // WebView2 renderer, which would otherwise leave the CDP call hanging.
    private static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(5);

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
        WebView2 webView)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "RegisterWebView2 must be called from a thread with a DispatcherQueue (typically the UI thread).");

        var coreWebView2 = webView.CoreWebView2;

        bridge.Register(
            resource,
            expression => DispatchEvalAsync(coreWebView2, dispatcherQueue, expression),
            clearCache => DispatchReloadAsync(coreWebView2, dispatcherQueue, clearCache),
            request => DispatchScreenshotAsync(webView, dispatcherQueue, request));
    }

    private static Task<string> DispatchEvalAsync(CoreWebView2 coreWebView2, DispatcherQueue dispatcherQueue, string expression)
    {
        var tcs = new TaskCompletionSource<string>();

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await coreWebView2.ExecuteScriptAsync(expression);
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

    private static Task DispatchReloadAsync(CoreWebView2 coreWebView2, DispatcherQueue dispatcherQueue, bool clearCache)
    {
        var tcs = new TaskCompletionSource();

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (clearCache)
                {
                    await coreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
                }

                coreWebView2.Reload();
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
        ScreenshotRequest request)
    {
        var tcs = new TaskCompletionSource<ScreenshotData>();

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                // Inactive tabs pause the renderer, so Page.captureScreenshot
                // would hang. Fail fast both before and after the settle delay.
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

                var coreWebView2 = webView.CoreWebView2;
                var paramsJson = BuildCaptureScreenshotParams(request);
                var captureTask = coreWebView2
                    .CallDevToolsProtocolMethodAsync("Page.captureScreenshot", paramsJson)
                    .AsTask();

                // Bounded wait so a tab switch mid-capture surfaces as a timeout
                // instead of an indefinite hang.
                var winner = await Task.WhenAny(captureTask, Task.Delay(ScreenshotCaptureTimeout));
                if (winner != captureTask)
                {
                    throw new TimeoutException(
                        $"Screenshot timed out after {ScreenshotCaptureTimeout.TotalSeconds:0}s. " +
                        "The document tab likely became inactive during capture, which pauses " +
                        "WebView2 rendering. Re-activate the tab and retry.");
                }

                var resultJson = await captureTask;
                using var doc = JsonDocument.Parse(resultJson);
                var base64 = doc.RootElement.GetProperty("data").GetString() ?? string.Empty;
                // Decode at the platform boundary so downstream stages carry raw
                // bytes. JSON envelopes can otherwise escape '+' and corrupt the payload.
                var bytes = Convert.FromBase64String(base64);

                int width;
                int height;
                if (request.Clip is not null)
                {
                    width = (int)Math.Round(request.Clip.Width * request.Clip.Scale);
                    height = (int)Math.Round(request.Clip.Height * request.Clip.Scale);
                }
                else
                {
                    width = 0;
                    height = 0;
                }

                tcs.TrySetResult(new ScreenshotData(request.Format, width, height, bytes));
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

    private static string BuildCaptureScreenshotParams(ScreenshotRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["format"] = request.Format
        };
        if (request.Format == "jpeg")
        {
            payload["quality"] = request.Quality;
        }
        if (request.Clip is not null)
        {
            payload["clip"] = new Dictionary<string, object>
            {
                ["x"] = request.Clip.X,
                ["y"] = request.Clip.Y,
                ["width"] = request.Clip.Width,
                ["height"] = request.Clip.Height,
                ["scale"] = request.Clip.Scale
            };
        }
        return JsonSerializer.Serialize(payload);
    }
}
