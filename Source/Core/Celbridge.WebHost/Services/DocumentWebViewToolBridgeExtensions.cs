using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Windows / WebView2 helpers that wrap the platform-agnostic
/// IDocumentWebViewToolBridge.Register call. Owns the UI-thread marshalling so
/// document views do not duplicate the eval-and-reload dispatch boilerplate.
/// </summary>
public static class DocumentWebViewToolBridgeExtensions
{
    /// <summary>
    /// Registers a CoreWebView2 instance with the tool bridge. Captures the current
    /// thread's DispatcherQueue and uses it to marshal eval and reload calls onto
    /// the UI thread for the lifetime of the registration. Must be called from a
    /// thread that has an associated DispatcherQueue (i.e. the UI thread).
    /// </summary>
    public static void RegisterCoreWebView2(
        this IDocumentWebViewToolBridge bridge,
        ResourceKey resource,
        CoreWebView2 coreWebView2)
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "RegisterCoreWebView2 must be called from a thread with a DispatcherQueue (typically the UI thread).");

        bridge.Register(
            resource,
            expression => DispatchEvalAsync(coreWebView2, dispatcherQueue, expression),
            clearCache => DispatchReloadAsync(coreWebView2, dispatcherQueue, clearCache));
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
}
