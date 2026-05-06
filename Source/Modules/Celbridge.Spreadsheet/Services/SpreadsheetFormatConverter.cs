using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Services;

/// <summary>
/// Converts FormatSpec values to ClosedXML style types. Unknown
/// color strings and unrecognised border-style names return failures so the
/// command can report them to the caller without saving the workbook.
/// </summary>
internal static class SpreadsheetFormatConverter
{
    /// <summary>
    /// Parses a CSS hex color string (#RRGGBB) into an XLColor. Returns
    /// failure for strings that ClosedXML cannot parse.
    /// </summary>
    public static Result<XLColor> ParseColor(string colorHex)
    {
        try
        {
            var color = XLColor.FromHtml(colorHex);
            return color;
        }
        catch
        {
            return Result.Fail($"Invalid color: '{colorHex}'. Use a CSS hex string such as '#FF0000'.");
        }
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
