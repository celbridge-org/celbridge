namespace Celbridge.WebHost;

/// <summary>
/// The web view services of one platform: its capability flags, web view creation, and the browsing data that all
/// web views share. Each head has its own implementation.
/// </summary>
public interface IWebViewPlatform
{
    /// <summary>
    /// True when a page loaded from a mapped virtual host has that virtual host as its origin.
    /// </summary>
    bool SupportsVirtualHostMapping { get; }

    /// <summary>
    /// True when the platform's web views have a find bar of their own, so the host adds none.
    /// </summary>
    bool ProvidesBuiltInFind { get; }

    /// <summary>
    /// True when a web view can give its page a viewport size before the view is laid out. On other platforms, a
    /// page reports a size only once it has been shown.
    /// </summary>
    bool CanSizeUnarrangedViewport { get; }

    /// <summary>
    /// True when browsing data can be cleared while the application runs.
    /// </summary>
    bool SupportsLiveBrowsingDataClear { get; }

    /// <summary>
    /// Whether the most recent mouse press landed in a web view. The value is already current when the managed
    /// pointer pipeline raises that press. False where the platform cannot tell.
    /// </summary>
    bool IsLastPressInWebView { get; }

    /// <summary>
    /// Creates a web view whose page is ready but has not navigated.
    /// </summary>
    Task<WebViewBase> CreateWebViewAsync();

    /// <summary>
    /// Clears the browsing data that every web view shares. Callers check SupportsLiveBrowsingDataClear first.
    /// Throws if the clear does not complete.
    /// </summary>
    Task ClearBrowsingDataAsync();
}
