using Celbridge.UserInterface.Helpers;

namespace Celbridge.Browser.ViewModels;

/// <summary>
/// Describes which items of a browser tab's context menu apply to the tab.
/// </summary>
public sealed record BrowserTabMenuState
{
    public TabCloseItems CloseItems { get; init; }

    /// <summary>
    /// Returns true when the tab has a web address to open again in a new tab.
    /// </summary>
    public bool CanDuplicate { get; init; }

    /// <summary>
    /// Returns true when a bookmark points at the tab's address. The bookmark item then removes that bookmark.
    /// </summary>
    public bool IsBookmarked { get; init; }

    /// <summary>
    /// Returns true when the bookmark item can add or remove a bookmark: the tab has a web address, and the file is
    /// writable.
    /// </summary>
    public bool CanToggleBookmark { get; init; }

    /// <summary>
    /// Returns true when the tab has an address to copy or to open in the system browser.
    /// </summary>
    public bool HasAddress { get; init; }
}
