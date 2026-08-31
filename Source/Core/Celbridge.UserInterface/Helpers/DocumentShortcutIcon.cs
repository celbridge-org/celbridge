namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the icon a document shortcut's Utility Rail button draws, so the settings card previewing the
/// button and the rail building it agree on what an unset icon means.
/// </summary>
public static class DocumentShortcutIcon
{
    /// <summary>
    /// The icon name for a shortcut: the one it names, or the default document icon when it names none.
    /// A name the icon set does not carry is returned as it is, the icon service resolving it to a
    /// fallback glyph.
    /// </summary>
    public static string Resolve(IIconService iconService, string iconName)
    {
        var trimmedName = iconName.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return iconService.GetIconName(IconSymbol.File);
        }

        return trimmedName;
    }
}
