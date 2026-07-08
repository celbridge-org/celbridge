using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// No-op IWebViewFocusMonitor for platforms where the managed GotFocus event fires for clicks inside
/// web view content, so no native focus signal is needed.
/// </summary>
public class NullWebViewFocusMonitor : IWebViewFocusMonitor
{
    public void Register(CoreWebView2 coreWebView, Action onFocusSignal)
    {
    }

    public void Unregister(CoreWebView2 coreWebView)
    {
    }
}
