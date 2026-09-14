namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the icon a document shortcut's Utility Rail button draws, so the settings card previewing the
/// button and the rail building it agree on what an unset icon means.
/// </summary>
public static class DocumentShortcutIcon
{
    /// <summary>
    /// The icon a shortcut naming none is drawn with.
    /// </summary>
    public const IconSymbol DefaultSymbol = IconSymbol.Pin;

    /// <summary>
    /// The icon name for a shortcut: the one it names, or the default shortcut icon when it names none.
    /// </summary>
    public static string Resolve(IIconService iconService, string iconName)
    {
        return IconNameResolver.Resolve(iconService, iconName, DefaultSymbol);
    }
}
