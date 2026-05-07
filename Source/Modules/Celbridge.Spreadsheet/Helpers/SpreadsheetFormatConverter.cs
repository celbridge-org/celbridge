using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Helpers;

/// <summary>
/// Converts FormatSpec values to ClosedXML style types. Unknown
/// color strings and unrecognised border-style names return failures so the
/// command can report them to the caller without saving the workbook.
/// </summary>
internal static class SpreadsheetFormatConverter
{
    /// <summary>
    /// Parses a CSS hex color string (#RRGGBB) into an XLColor. Strictly
    /// requires the 6-digit form: ClosedXML's XLColor.FromHtml accepts the
    /// 3-digit shorthand and CSS named colors as well, but the spec for the
    /// spreadsheet tools is #RRGGBB only and accepting other forms makes the
    /// contract inconsistent (named colors are rejected, shorthand was not).
    /// </summary>
    public static Result<XLColor> ParseColor(string colorHex)
    {
        if (!IsSixDigitHex(colorHex))
        {
            return Result.Fail($"Invalid color: '{colorHex}'. Use a 6-digit CSS hex string such as '#FF0000'.");
        }

        try
        {
            var color = XLColor.FromHtml(colorHex);
            return color;
        }
        catch
        {
            return Result.Fail($"Invalid color: '{colorHex}'. Use a 6-digit CSS hex string such as '#FF0000'.");
        }
    }

    private static bool IsSixDigitHex(string colorHex)
    {
        if (colorHex.Length != 7)
        {
            return false;
        }

        if (colorHex[0] != '#')
        {
            return false;
        }

        for (int characterIndex = 1; characterIndex < 7; characterIndex++)
        {
            var character = colorHex[characterIndex];
            var isHexDigit = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Maps a border style name to XLBorderStyleValues. Accepts the spec names
    /// (SOLID, DASHED, DOTTED, DOUBLE, NONE) and falls back to case-insensitive
    /// XLBorderStyleValues enum parsing for other ClosedXML names.
    /// </summary>
    public static Result<XLBorderStyleValues> ParseBorderStyle(string style)
    {
        var mapped = style.ToUpperInvariant() switch
        {
            "SOLID" => (XLBorderStyleValues?)XLBorderStyleValues.Thin,
            "DASHED" => XLBorderStyleValues.Dashed,
            "DOTTED" => XLBorderStyleValues.Dotted,
            "DOUBLE" => XLBorderStyleValues.Double,
            "NONE" => XLBorderStyleValues.None,
            _ => null
        };

        if (mapped.HasValue)
        {
            return mapped.Value;
        }

        if (Enum.TryParse<XLBorderStyleValues>(style, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return Result.Fail(
            $"Unknown border style: '{style}'. Use SOLID, DASHED, DOTTED, DOUBLE, NONE, or a ClosedXML XLBorderStyleValues name.");
    }

    /// <summary>
    /// Maps a horizontal alignment name to XLAlignmentHorizontalValues. Accepts
    /// LEFT, CENTER, RIGHT, GENERAL, JUSTIFY and falls back to enum parsing.
    /// </summary>
    public static Result<XLAlignmentHorizontalValues> ParseHorizontalAlignment(string alignment)
    {
        var mapped = alignment.ToUpperInvariant() switch
        {
            "LEFT" => (XLAlignmentHorizontalValues?)XLAlignmentHorizontalValues.Left,
            "CENTER" => XLAlignmentHorizontalValues.Center,
            "RIGHT" => XLAlignmentHorizontalValues.Right,
            "GENERAL" => XLAlignmentHorizontalValues.General,
            "JUSTIFY" => XLAlignmentHorizontalValues.Justify,
            _ => null
        };

        if (mapped.HasValue)
        {
            return mapped.Value;
        }

        if (Enum.TryParse<XLAlignmentHorizontalValues>(alignment, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return Result.Fail(
            $"Unknown horizontal alignment: '{alignment}'. Use LEFT, CENTER, RIGHT, GENERAL, or JUSTIFY.");
    }

    /// <summary>
    /// Maps a vertical alignment name to XLAlignmentVerticalValues. Accepts
    /// TOP, MIDDLE (maps to Center), BOTTOM and falls back to enum parsing.
    /// </summary>
    public static Result<XLAlignmentVerticalValues> ParseVerticalAlignment(string alignment)
    {
        var mapped = alignment.ToUpperInvariant() switch
        {
            "TOP" => (XLAlignmentVerticalValues?)XLAlignmentVerticalValues.Top,
            "MIDDLE" => XLAlignmentVerticalValues.Center,
            "BOTTOM" => XLAlignmentVerticalValues.Bottom,
            _ => null
        };

        if (mapped.HasValue)
        {
            return mapped.Value;
        }

        if (Enum.TryParse<XLAlignmentVerticalValues>(alignment, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return Result.Fail(
            $"Unknown vertical alignment: '{alignment}'. Use TOP, MIDDLE, or BOTTOM.");
    }
}
