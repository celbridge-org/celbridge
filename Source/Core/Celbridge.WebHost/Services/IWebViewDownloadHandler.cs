namespace Celbridge.WebHost;

/// <summary>
/// Routes one web view's downloads through the download service. The view creates it when it is handed out, and
/// detaches it when it closes.
/// </summary>
public interface IWebViewDownloadHandler
{
    /// <summary>
    /// Raised when a download starts. A navigation that becomes a download then ends without a page, and a
    /// surface has to tell that apart from a page that failed to load. Raised only for a download that
    /// replaced the main frame's navigation, and before that navigation ends where the platform allows.
    /// </summary>
    event EventHandler? DownloadStarted;

    /// <summary>
    /// Stops routing the web view's downloads. Where a transfer cannot outlive the web view that started
    /// it, this stops the ones still running and settles their records, because nothing would ever report
    /// their outcome. No other web view's downloads are touched.
    /// </summary>
    void Detach();
}
