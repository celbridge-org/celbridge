using Celbridge.WebHost;

namespace Celbridge.WebView.Views;

/// <summary>
/// The web content that a WebViewFindBar drives. The host implements this so the bar can run find without
/// knowing about the underlying WebView, keeping the bar reusable and free of WebView2 specifics.
/// </summary>
public interface IWebViewFindTarget
{
    Task StartFindAsync(string term, FindOptions options);

    void FindNext();

    void FindPrevious();

    void StopFind();
}
