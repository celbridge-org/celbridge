namespace Celbridge.Browser.ViewModels;

/// <summary>
/// One open tab in a browser document's editor state: the page's address, title and icon. The icon is the tab's PNG
/// icon, or null for a tab that showed the default icon. A tab only shows PNG icons that pass the .browser file's
/// check. Before the page loads, a restored tab shows the saved title and icon.
/// </summary>
public sealed record BrowserTabState(string Url, string Title, byte[]? Favicon = null);

/// <summary>
/// The per-user view state of a browser document: whether the settings are open, which section they show, the open tabs
/// and the shown tab. The document editor state saves this, and the .browser file does not.
/// </summary>
public sealed record BrowserEditorState(
    bool SettingsOpen,
    string SettingsSectionKey,
    IReadOnlyList<BrowserTabState>? Tabs = null,
    int ShownTabIndex = 0);
