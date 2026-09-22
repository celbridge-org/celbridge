using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Receives the address a page has committed to: a new page taking the place of the one on screen, or the
/// same page moving to another address of its own, as a fragment link does. A navigation that never commits,
/// such as one that becomes a download, is never reported.
/// </summary>
public delegate void NavigationCommitted(string url);

/// <summary>
/// Reports each change of CoreWebView2.Source, which WebView2 makes as a navigation commits, until disposed.
/// </summary>
internal sealed class SourceChangedObserver : IDisposable
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly NavigationCommitted _onCommitted;

    public SourceChangedObserver(CoreWebView2 coreWebView2, NavigationCommitted onCommitted)
    {
        _coreWebView2 = coreWebView2;
        _onCommitted = onCommitted;

        _coreWebView2.SourceChanged += CoreWebView2_SourceChanged;
    }

    private void CoreWebView2_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        // CoreWebView2.Source rather than WebView2.Source, which reports the address percent-encoded where
        // this reports it as the page shows it.
        _onCommitted(sender.Source);
    }

    public void Dispose()
    {
        _coreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
    }
}
