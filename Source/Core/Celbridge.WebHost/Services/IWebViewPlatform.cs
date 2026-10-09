namespace Celbridge.WebHost;

/// <summary>
/// What the web views of one platform share: their capabilities, the browsing data they all use, and how each
/// is created. Each head has its own implementation.
/// </summary>
public interface IWebViewPlatform
{
    /// <summary>
    /// True when a page a web view loads from a mapped virtual host has that host as its origin.
    /// </summary>
    bool SupportsVirtualHostMapping { get; }

    /// <summary>
    /// True when the platform's web views have a find bar of their own, so the host adds none.
    /// </summary>
    bool ProvidesBuiltInFind { get; }

    /// <summary>
    /// True when a web view can give its page a viewport size before it is laid out. Where it cannot, a page
    /// that has not been shown has no size to report.
    /// </summary>
    bool CanSizeUnarrangedViewport { get; }

    /// <summary>
    /// True when browsing data can be cleared while the application runs.
    /// </summary>
    bool SupportsLiveBrowsingDataClear { get; }

    /// <summary>
    /// Creates a web view whose page is ready but has not navigated.
    /// </summary>
    Task<WebViewBase> CreateWebViewAsync();

    /// <summary>
    /// Clears the cookies, cached credentials, site data and HTTP cache that every web view shares. Does nothing
    /// where SupportsLiveBrowsingDataClear is false. Throws if the clear does not complete.
    /// </summary>
    Task ClearBrowsingDataAsync();
}
