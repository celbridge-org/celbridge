using System.Globalization;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Adjusts an icon colour so every file type reads with a similar, legible prominence against the panel
/// background. A colour is nudged into a per-theme luminance band, keeping its hue and saturation: a dark
/// brand colour (a navy, a bottle green) is lifted so it is not lost against a dark panel, and a glaring
/// one (a bright yellow) is brought down so it does not dominate. Legibility is favoured over exact brand
/// fidelity by design.
/// </summary>
public static class IconColorLegibility
{
    // The luminance band each theme normalises into. The floor keeps a colour clear of the background;
    // the ceiling stops a bright colour drawing the eye more than its neighbours. Values are WCAG
    // relative luminance (0 = black, 1 = white).
    private const double DarkThemeFloor = 0.38;
    private const double DarkThemeCeiling = 0.52;
    private const double LightThemeFloor = 0.10;
    private const double LightThemeCeiling = 0.22;

    // Rounding an adjusted colour back to bytes can land its luminance a hair outside the band, which
    // would make a second pass nudge it again. This slack keeps the adjustment idempotent.
    private const double BandTolerance = 0.006;

    // Moving a colour's lightness toward the band washes it out, so an adjusted chromatic colour has its
    // saturation lifted to this floor to stay vivid. A near-grey colour (below the threshold) is left
    // neutral rather than tinted.
    private const double MinChromaticSaturation = 0.70;
    private const double GrayscaleThreshold = 0.10;

    /// <summary>
    /// Returns a hex colour whose luminance sits within the themed band, adjusting only lightness. A
    /// colour already in band, or a string that is not a hex colour, is returned unchanged. The input
    /// format is preserved: an "#AARRGGBB" colour keeps its alpha.
    /// </summary>
    public static string Normalize(string colorHex, bool darkBackground)
    {
        if (!TryParse(colorHex, out var alpha, out var red, out var green, out var blue))
        {
            return colorHex;
        }

        var floor = darkBackground ? DarkThemeFloor : LightThemeFloor;
        var ceiling = darkBackground ? DarkThemeCeiling : LightThemeCeiling;

        var luminance = RelativeLuminance(red, green, blue);

        double target;
        if (luminance < floor - BandTolerance)
        {
            target = floor;
        }
        else if (luminance > ceiling + BandTolerance)
        {
            target = ceiling;
        }
        else
        {
            return colorHex;
        }

        RgbToHsl(red, green, blue, out var hue, out var saturation, out var lightness);

        var adjustedSaturation = BoostSaturation(saturation);
        var adjustedLightness = LightnessForLuminance(hue, adjustedSaturation, target);
        HslToRgb(hue, adjustedSaturation, adjustedLightness, out var adjustedRed, out var adjustedGreen, out var adjustedBlue);

        return Format(alpha, adjustedRed, adjustedGreen, adjustedBlue);
    }

    private static double BoostSaturation(double saturation)
    {
        if (saturation < GrayscaleThreshold)
        {
            return saturation;
        }

        return Math.Min(1.0, Math.Max(saturation, MinChromaticSaturation));
    }

    // Luminance rises monotonically with HSL lightness for a fixed hue and saturation, so a binary search
    // finds the lightness that hits the target luminance.
    private static double LightnessForLuminance(double hue, double saturation, double targetLuminance)
    {
        double low = 0.0;
        double high = 1.0;

        for (var iteration = 0; iteration < 24; iteration++)
        {
            var mid = (low + high) / 2.0;
            HslToRgb(hue, saturation, mid, out var r, out var g, out var b);

            if (RelativeLuminance(r, g, b) < targetLuminance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2.0;
    }

    private static bool TryParse(string colorHex, out byte alpha, out byte red, out byte green, out byte blue)
    {
        alpha = 255;
        red = green = blue = 0;

        if (string.IsNullOrEmpty(colorHex) || colorHex[0] != '#')
        {
            return false;
        }

        var digits = colorHex.Substring(1);
        if (digits.Length != 6 && digits.Length != 8)
        {
            return false;
        }

        var offset = 0;
        if (digits.Length == 8)
        {
            if (!TryHexByte(digits, 0, out alpha))
            {
                return false;
            }
            offset = 2;
        }

        return TryHexByte(digits, offset, out red)
            && TryHexByte(digits, offset + 2, out green)
            && TryHexByte(digits, offset + 4, out blue);
    }

    private static bool TryHexByte(string digits, int start, out byte value)
    {
        return byte.TryParse(digits.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string Format(byte alpha, byte red, byte green, byte blue)
    {
        if (alpha == 255)
        {
            return $"#{red:X2}{green:X2}{blue:X2}";
        }

        return $"#{alpha:X2}{red:X2}{green:X2}{blue:X2}";
    }

    private static double RelativeLuminance(byte red, byte green, byte blue)
    {
        return 0.2126 * LinearChannel(red)
            + 0.7152 * LinearChannel(green)
            + 0.0722 * LinearChannel(blue);
    }

    private static double LinearChannel(byte channel)
    {
        var value = channel / 255.0;

        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static void RgbToHsl(byte red, byte green, byte blue, out double hue, out double saturation, out double lightness)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        lightness = (max + min) / 2.0;

        if (delta == 0.0)
        {
            hue = 0.0;
            saturation = 0.0;
            return;
        }

        saturation = delta / (1.0 - Math.Abs(2.0 * lightness - 1.0));

        if (max == r)
        {
            hue = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            hue = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (hue < 0.0)
        {
            hue += 360.0;
        }
    }

    private static void HslToRgb(double hue, double saturation, double lightness, out byte red, out byte green, out byte blue)
    {
        var chroma = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        var secondary = chroma * (1.0 - Math.Abs(((hue / 60.0) % 2.0) - 1.0));
        var match = lightness - chroma / 2.0;

        double r, g, b;
        if (hue < 60.0)
        {
            (r, g, b) = (chroma, secondary, 0.0);
        }
        else if (hue < 120.0)
        {
            (r, g, b) = (secondary, chroma, 0.0);
        }
        else if (hue < 180.0)
        {
            (r, g, b) = (0.0, chroma, secondary);
        }
        else if (hue < 240.0)
        {
            (r, g, b) = (0.0, secondary, chroma);
        }
        else if (hue < 300.0)
        {
            (r, g, b) = (secondary, 0.0, chroma);
        }
        else
        {
            (r, g, b) = (chroma, 0.0, secondary);
        }

        red = ToByte(r + match);
        green = ToByte(g + match);
        blue = ToByte(b + match);
    }

    private static byte ToByte(double channel)
    {
        var scaled = (int)Math.Round(channel * 255.0);

        return (byte)Math.Clamp(scaled, 0, 255);
    }
}
