// Compiled only under WINDOWS, so the Skia build never links against the WinAppSDK WebView2 surface. The DI
// selection is gated on the same symbol.
#if WINDOWS
using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// The web view platform of the packaged Windows head, which drives the WebView2 SDK directly.
/// </summary>
internal sealed class WindowsWebViewPlatform : IWebViewPlatform
{
    private readonly ILogger<WindowsWebView> _webViewLogger;

    public WindowsWebViewPlatform(ILogger<WindowsWebView> webViewLogger)
    {
        _webViewLogger = webViewLogger;
    }

    public bool SupportsVirtualHostMapping => true;

    // Chromium's WebView2 ships its own find bar, which Ctrl+F reaches directly.
    public bool ProvidesBuiltInFind => true;

    public bool CanSizeUnarrangedViewport => false;

    public bool SupportsLiveBrowsingDataClear => true;

    // A press in a web view never reaches the managed tree here, since the page lives in its own child window.
    public bool IsLastPressInWebView => false;

    public async Task<WebViewBase> CreateWebViewAsync()
    {
        // A transparent background stops the view showing white for a moment when its tab is switched to. Similar
        // issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        var control = new WebView2
        {
            DefaultBackgroundColor = Colors.Transparent
        };

        // The packaged WebView2 initializes without being attached to the visual tree.
        await control.EnsureCoreWebView2Async();

        return new WindowsWebView(control, this, _webViewLogger);
    }

    // The shared profile hangs off a live CoreWebView2, so a web view is created to reach it and closed after.
    public async Task ClearBrowsingDataAsync()
    {
        var control = new WebView2();
        try
        {
            await control.EnsureCoreWebView2Async();

            // These three kinds clear exactly the cookies, cached credentials, site data and HTTP cache that the
            // action promises. AllSite covers cookies and the DOM storage kinds. Browsing history, download history
            // and the profile's own settings are deliberately left alone.
            await control.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.AllSite |
                CoreWebView2BrowsingDataKinds.PasswordAutosave |
                CoreWebView2BrowsingDataKinds.DiskCache);
        }
        finally
        {
            control.Close();
        }
    }
}
#endif
