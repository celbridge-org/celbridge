using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
#if WINDOWS
using System.Text.Json;
#else
using Celbridge.WebHost.Services;
#endif

namespace Celbridge.WebHost;

/// <summary>
/// WebView2 platform helpers for IDocumentWebViewToolBridge. Marshals eval,
/// reload, and screenshot calls onto the UI thread.
/// </summary>
public static class DocumentWebViewToolBridgeExtensions
{
#if WINDOWS
    // Bounds the wait for Page.captureScreenshot. Inactive WinUI tabs pause the
    // WebView2 renderer, which would otherwise leave the CDP call hanging.
    private static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(5);
#endif

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
#if WINDOWS
                var result = await coreWebView2.ExecuteScriptAsync(expression);
                tcs.TrySetResult(result);
#else
                // WKWebView's evaluateJavaScript faults on JS exceptions and syntax errors (WKError 4), on
                // unsupported return types such as Promises (WKError 5), and on an undefined result
                // (surfaced by Uno as an ArgumentNullException). WebView2 returns the JSON literal "null"
                // silently in the equivalent cases. Normalise the faults so common errors and undefined
                // results read as None on Python callers across platforms. Best-effort: exotic return
                // values (Promise, Date, NaN, circular references) may still serialise differently per
                // platform. Agents are expected to adapt to the result they get.
                try
                {
                    var result = await coreWebView2.ExecuteScriptAsync(expression);
                    tcs.TrySetResult(result ?? "null");
                }
                catch (ArgumentNullException)
                {
                    tcs.TrySetResult("null");
                }
                catch (Exception scriptEx) when (scriptEx.Message.Contains("WKErrorDomain", StringComparison.Ordinal))
                {
                    tcs.TrySetResult("null");
                }
#endif
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
#if WINDOWS
                if (clearCache)
                {
                    await coreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
                }

                coreWebView2.Reload();
#else
                // Skia: CoreWebView2.Profile (cache clear) and Reload() are not implemented, so reload
                // through the page. clearCache is best-effort here -- location.reload() does not purge the
                // HTTP cache (that would need WKWebsiteDataStore interop).
                await coreWebView2.ExecuteScriptAsync("location.reload()");
#endif
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

#if WINDOWS
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
#else
                // Skia: Page.captureScreenshot (CDP) is not implemented, so snapshot the native WKWebView.
                // The bridge's clip and MaxEdge scaling are not applied here, so the capture is the full
                // visible surface at native resolution, encoded to the requested format.
                if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out var nativeHandle, out var detail))
                {
                    throw new InvalidOperationException(
                        $"Could not resolve the native WKWebView for a screenshot. Walked: {detail}");
                }

                // The bridge resolves the clip rect (viewport or selector) and a Scale that fits MaxEdge.
                // Map that to the native snapshot: clip to the rect, render at Width * Scale points.
                var clip = request.Clip;
                var snapshotRequest = new MacSnapshotRequest(
                    clip?.X ?? 0,
                    clip?.Y ?? 0,
                    clip?.Width ?? 0,
                    clip?.Height ?? 0,
                    clip is not null ? clip.Width * clip.Scale : 0,
                    request.Format,
                    request.Quality);

                var snapshot = await MacOSWebViewInterop.TakeSnapshotAsync(nativeHandle, snapshotRequest);
                if (snapshot is null)
                {
                    throw new InvalidOperationException(
                        "The native WKWebView snapshot did not complete. The document tab must be the " +
                        "active, visible tab for a screenshot.");
                }

                tcs.TrySetResult(new ScreenshotData(request.Format, snapshot.Width, snapshot.Height, snapshot.Bytes));
#endif
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

#if WINDOWS
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
#endif
}
