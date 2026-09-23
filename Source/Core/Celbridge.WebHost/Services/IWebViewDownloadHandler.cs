namespace Celbridge.WebHost;

/// <summary>
/// Routes one hosted web view's downloads through the download service, from when a surface attaches it
/// until the surface detaches it. Created by IWebViewAdapter.AttachDownloadHandler.
/// </summary>
public interface IWebViewDownloadHandler
{
    /// <summary>
    /// Raised when a download starts. A navigation whose response turns out to be an attachment becomes a
    /// download and then ends without a page, so a surface that navigates needs to tell that apart from a
    /// page that failed to load. Where the platform decides on the download before that navigation ends,
    /// this is raised first, and only for a download that replaced the main frame's navigation.
    /// </summary>
    event EventHandler? DownloadStarted;

    /// <summary>
    /// Stops routing the web view's downloads. Where a transfer cannot outlive the web view that started
    /// it, the ones this web view is running are stopped and their records settle, since nothing would ever
    /// report their outcome. Only this web view's downloads are touched.
    /// </summary>
    void Detach();
}
