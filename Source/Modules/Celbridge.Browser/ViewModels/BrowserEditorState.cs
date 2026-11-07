namespace Celbridge.Browser.ViewModels;

/// <summary>
/// One open tab in a browser document's editor state: the page's address, title and icon address. Before the page
/// loads, a restored tab shows the saved title, and the cached icon for the saved address.
/// </summary>
public sealed record BrowserTabState(string Url, string Title, string FaviconAddress = "");

/// <summary>
/// The per-user view state of a browser document: whether the settings are open, which section they show, the open tabs
/// and the shown tab. The document editor state saves this, and the .browser file does not.
/// </summary>
public sealed record BrowserEditorState(
    bool SettingsOpen,
    string SettingsSectionKey,
    IReadOnlyList<BrowserTabState>? Tabs = null,
    int ShownTabIndex = 0);
