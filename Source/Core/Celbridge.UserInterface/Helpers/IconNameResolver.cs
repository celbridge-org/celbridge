namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the icon name a surface draws for what a user-configurable icon field holds, so the field
/// previewing the icon and the surface drawing it cannot disagree about what an unset name means.
/// </summary>
public static class IconNameResolver
{
    /// <summary>
    /// The icon name to draw: the one the field names, or the surface's own default when it names none.
    /// A name the icon set does not carry is returned as it is, the icon service resolving it to a
    /// fallback glyph. Pass no default symbol where an unset name means no glyph at all.
    /// </summary>
    public static string Resolve(IIconService iconService, string iconName, IconSymbol? defaultSymbol)
    {
        var trimmedName = iconName.Trim();
        if (!string.IsNullOrEmpty(trimmedName))
        {
            return trimmedName;
        }

        if (defaultSymbol is null)
        {
            return string.Empty;
        }

        return iconService.GetIconName(defaultSymbol.Value);
    }
}
