using Celbridge.WebHost;

namespace Celbridge.Browser.Views;

/// <summary>
/// Web content for a BrowserFindBar to drive. The document view implements this, so the bar can run find without any
/// knowledge of the web view behind the content.
/// </summary>
public interface IBrowserFindTarget
{
    Task StartFindAsync(string term, FindOptions options);

    void FindNext();

    void FindPrevious();

    void StopFind();
}
