using System.Globalization;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Converts the hex colour strings carried by icon definitions and palettes into drawable colours.
/// </summary>
public static class HexColor
{
    /// <summary>
    /// Parses an "#RRGGBB" or "#AARRGGBB" colour, with or without its leading hash. A colour without an
    /// alpha component is fully opaque.
    /// </summary>
    public static Windows.UI.Color Parse(string colorHex)
    {
        var digits = colorHex.TrimStart('#');

        byte alpha = 255;
        var offset = 0;
        if (digits.Length == 8)
        {
            alpha = byte.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber);
            offset = 2;
        }

        var red = byte.Parse(digits.AsSpan(offset, 2), NumberStyles.HexNumber);
        var green = byte.Parse(digits.AsSpan(offset + 2, 2), NumberStyles.HexNumber);
        var blue = byte.Parse(digits.AsSpan(offset + 4, 2), NumberStyles.HexNumber);

        return Windows.UI.Color.FromArgb(alpha, red, green, blue);
    }
}
