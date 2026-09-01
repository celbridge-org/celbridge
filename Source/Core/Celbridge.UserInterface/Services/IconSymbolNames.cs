namespace Celbridge.UserInterface.Services;

/// <summary>
/// The prefixed icon name each IconSymbol is drawn with. A symbol names an icon the UI reaches for by role
/// rather than by name, so the two can be changed independently.
/// </summary>
internal static class IconSymbolNames
{
    // Add new common icons here. Anything not listed is still resolvable by name.
    private static readonly Dictionary<IconSymbol, string> _iconNamesBySymbol = new()
    {
        { IconSymbol.Close, "bs-x-lg" },
        { IconSymbol.Search, "bs-search" },
        { IconSymbol.Folder, "bs-folder" },
        { IconSymbol.FolderOpen, "bs-folder2-open" },
        { IconSymbol.FolderFilled, "bs-folder-fill" },
        { IconSymbol.FolderAdd, "bs-folder-plus" },
        { IconSymbol.FileAdd, "bs-file-earmark-plus" },
        { IconSymbol.File, "bs-file-earmark" },
        { IconSymbol.Bug, "bs-bug" },
        { IconSymbol.Back, "bs-arrow-left" },
        { IconSymbol.Forward, "bs-arrow-right" },
        { IconSymbol.Home, "bs-house" },
        { IconSymbol.Refresh, "bs-arrow-clockwise" },
        { IconSymbol.Reveal, "bs-box-arrow-up-right" },
        { IconSymbol.Dock, "bs-box-arrow-in-up-right" },
        { IconSymbol.Delete, "bs-trash" },
        { IconSymbol.Error, "bs-exclamation-circle-fill" },
        { IconSymbol.Warning, "bs-exclamation-triangle-fill" },
        { IconSymbol.Report, "bs-clipboard-data" },
        { IconSymbol.More, "bs-three-dots" },
        { IconSymbol.Collapse, "bs-arrows-collapse" },
        { IconSymbol.Settings, "bs-gear" },
        { IconSymbol.Sliders, "bs-sliders" },
        { IconSymbol.Windowed, "bs-window" },
        { IconSymbol.FullScreen, "bs-arrows-fullscreen" },
        { IconSymbol.FocusMode, "bs-fullscreen" },
        { IconSymbol.Presentation, "bs-easel" },
        { IconSymbol.Save, "bs-floppy" },
        { IconSymbol.ExitFullScreen, "bs-fullscreen-exit" },
        { IconSymbol.People, "bs-people" },
        { IconSymbol.Chat, "bs-chat-dots" },
        { IconSymbol.Upload, "bs-upload" },
        { IconSymbol.ChevronDown, "bs-chevron-down" },
        { IconSymbol.ChevronLeft, "bs-chevron-left" },
        { IconSymbol.ChevronRight, "bs-chevron-right" },
        { IconSymbol.ChevronUp, "bs-chevron-up" },
        { IconSymbol.MatchCase, "bs-type" },
        { IconSymbol.Replace, "bs-arrow-left-right" },
        { IconSymbol.Add, "bs-plus-lg" },
        { IconSymbol.Copy, "bs-copy" },
        { IconSymbol.Cut, "bs-scissors" },
        { IconSymbol.Paste, "bs-clipboard" },
        { IconSymbol.Rename, "bs-pencil" },
        { IconSymbol.Archive, "bs-archive" },
        { IconSymbol.Unarchive, "bs-box-arrow-up" },
        { IconSymbol.Recent, "bs-clock-history" },
        { IconSymbol.Menu, "bs-list" },
        { IconSymbol.Play, "bs-play-fill" },
        { IconSymbol.Examples, "bs-collection" },
        { IconSymbol.Book, "bs-book" },
        { IconSymbol.Link, "bs-link-45deg" },
        { IconSymbol.Exit, "bs-box-arrow-right" }
    };

    /// <summary>
    /// Looks up the prefixed icon name for a symbol, returning false for a symbol no name is listed for.
    /// </summary>
    public static bool TryGetIconName(IconSymbol icon, out string iconName)
    {
        if (_iconNamesBySymbol.TryGetValue(icon, out var name))
        {
            iconName = name;
            return true;
        }

        iconName = string.Empty;

        return false;
    }
}
