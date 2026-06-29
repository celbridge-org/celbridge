using Microsoft.Web.WebView2.Core;
#if !WINDOWS
using Celbridge.WebHost.Services;
#endif

namespace Celbridge.WebHost;

/// <summary>
/// Cross-platform document-start script injection for a WebView2. Hides the Windows-vs-Skia split:
/// the managed AddScriptToExecuteOnDocumentCreatedAsync is unimplemented on the Uno Skia heads, where
/// a native WKUserScript is installed instead.
/// </summary>
public static class WebViewDocumentStartScriptExtensions
{
    /// <summary>
    /// Installs a script to run at document-start, before page scripts, on every navigation. Used for the
    /// tool bridge shim that must wrap console/fetch before the page boots. On the Skia heads the native
    /// WKUserScript covers document-start; pair this with ReinjectDocumentStartScriptAsync from a
    /// NavigationCompleted handler for shims whose call-time hooks must be re-delivered per navigation.
    /// </summary>
    public static async Task InstallDocumentStartScriptAsync(this CoreWebView2 coreWebView2, string script)
    {
#if WINDOWS
        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
#else
        if (OperatingSystem.IsMacOS()
            && MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out _))
        {
            MacOSWebViewInterop.AddUserScriptAtDocumentStart(nativeHandle, script);
        }

        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// Re-delivers a document-start script after a navigation completes. No-op on Windows, where the
    /// managed document-start script persists across navigations; on the Skia heads it re-runs the script
    /// via ExecuteScriptAsync.
    /// </summary>
    public static async Task ReinjectDocumentStartScriptAsync(this CoreWebView2 coreWebView2, string script)
    {
#if WINDOWS
        await Task.CompletedTask;
#else
        await coreWebView2.ExecuteScriptAsync(script);
#endif
    }
}
