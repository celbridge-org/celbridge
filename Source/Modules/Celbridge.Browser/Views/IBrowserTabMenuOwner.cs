using Celbridge.Browser.ViewModels;

namespace Celbridge.Browser.Views;

/// <summary>
/// An item in a browser tab's context menu.
/// </summary>
public enum BrowserTabMenuAction
{
    Close,
    CloseOthers,
    CloseToTheRight,
    CloseToTheLeft,
    CloseAll,
    Duplicate,
    ToggleBookmark,
    CopyAddress,
    OpenInSystemBrowser
}

/// <summary>
/// Runs the items of each browser tab's context menu, including a close, which also releases the closed tab's web
/// view.
/// </summary>
public interface IBrowserTabMenuOwner
{
    /// <summary>
    /// Returns which menu items apply to a tab. The menu calls this each time it opens.
    /// </summary>
    BrowserTabMenuState GetTabMenuState(BrowserTabViewModel tab);

    /// <summary>
    /// Runs a menu item on a tab. The tab may be hidden.
    /// </summary>
    void RunTabMenuAction(BrowserTabViewModel tab, BrowserTabMenuAction action);

    /// <summary>
    /// Gives the keyboard back to the document after a tab's menu closes.
    /// </summary>
    void ReturnKeyboardAfterTabMenu();
}
