using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Services;

internal static class SpreadsheetFormatReader
{
    public static FormatSpec ReadFormatFromCell(IXLCell cell)
    {
        var style = cell.Style;

        var textFormat = ReadTextFormat(style);
        var backgroundColor = ReadBackgroundColor(style);
        var borders = ReadBorders(style);
        var horizontalAlignment = ReadHorizontalAlignment(style.Alignment.Horizontal);
        var verticalAlignment = ReadVerticalAlignment(style.Alignment.Vertical);
        bool? wrapText = style.Alignment.WrapText ? true : null;
        string? numberFormat = string.IsNullOrEmpty(style.NumberFormat.Format) ? null : style.NumberFormat.Format;
        bool? mergeRange = cell.IsMerged() ? true : null;

        return new FormatSpec(
            TextFormat: textFormat,
            BackgroundColor: backgroundColor,
            Borders: borders,
            HorizontalAlignment: horizontalAlignment,
            VerticalAlignment: verticalAlignment,
            WrapText: wrapText,
            NumberFormat: numberFormat,
            MergeRange: mergeRange);
    }

    private static TextFormat ReadTextFormat(IXLStyle style)
    {
        bool? bold = style.Font.Bold ? true : null;
        bool? italic = style.Font.Italic ? true : null;
        bool? underline = style.Font.Underline != XLFontUnderlineValues.None ? true : null;
        bool? strikethrough = style.Font.Strikethrough ? true : null;
        string? fontFamily = string.IsNullOrEmpty(style.Font.FontName) ? null : style.Font.FontName;
        double? fontSize = style.Font.FontSize > 0 ? style.Font.FontSize : null;
        string? foregroundColor = ReadColor(style.Font.FontColor);

        return new TextFormat(
            Bold: bold,
            Italic: italic,
            Underline: underline,
            Strikethrough: strikethrough,
            FontFamily: fontFamily,
            FontSize: fontSize,
            ForegroundColor: foregroundColor);
    }

    private static string? ReadBackgroundColor(IXLStyle style)
    {
        if (style.Fill.PatternType != XLFillPatternValues.Solid)
        {
            // Empty string is the clear-fill sentinel that round-trips through
            // spreadsheet_format_ranges as "no fill".
            return string.Empty;
        }

        return ReadColor(style.Fill.BackgroundColor);
    }

    private static BordersSpec? ReadBorders(IXLStyle style)
    {
        var top = ReadBorderSide(style.Border.TopBorder, style.Border.TopBorderColor);
        var bottom = ReadBorderSide(style.Border.BottomBorder, style.Border.BottomBorderColor);
        var left = ReadBorderSide(style.Border.LeftBorder, style.Border.LeftBorderColor);
        var right = ReadBorderSide(style.Border.RightBorder, style.Border.RightBorderColor);

        if (top is null && bottom is null && left is null && right is null)
        {
            return null;
        }

        return new BordersSpec(Top: top, Bottom: bottom, Left: left, Right: right);
    }

    private static BorderSide? ReadBorderSide(XLBorderStyleValues borderStyle, XLColor color)
    {
        var styleString = ReadBorderStyle(borderStyle);
        if (styleString is null)
        {
            return null;
        }

        var colorString = ReadColor(color);
        return new BorderSide(Style: styleString, Color: colorString);
    }

    private static string? ReadBorderStyle(XLBorderStyleValues borderStyle)
    {
        return borderStyle switch
        {
            XLBorderStyleValues.None => null,
            XLBorderStyleValues.Thin => "SOLID",
            XLBorderStyleValues.Dashed => "DASHED",
            XLBorderStyleValues.Dotted => "DOTTED",
            XLBorderStyleValues.Double => "DOUBLE",
            _ => borderStyle.ToString().ToUpperInvariant()
        };
    }

    private static string? ReadHorizontalAlignment(XLAlignmentHorizontalValues alignment)
    {
        return alignment switch
        {
            XLAlignmentHorizontalValues.General => null,
            XLAlignmentHorizontalValues.Left => "LEFT",
            XLAlignmentHorizontalValues.Center => "CENTER",
            XLAlignmentHorizontalValues.Right => "RIGHT",
            XLAlignmentHorizontalValues.Justify => "JUSTIFY",
            _ => alignment.ToString().ToUpperInvariant()
        };
    }

    private static string? ReadVerticalAlignment(XLAlignmentVerticalValues alignment)
    {
        return alignment switch
        {
            XLAlignmentVerticalValues.Bottom => null,
            XLAlignmentVerticalValues.Center => "MIDDLE",
            XLAlignmentVerticalValues.Top => "TOP",
            _ => alignment.ToString().ToUpperInvariant()
        };
    }

    private static string ReadColor(XLColor color)
    {
        if (color.ColorType != XLColorType.Color)
        {
            // Theme, indexed, or auto colours are represented as the empty
            // string so that round-tripping a default-coloured cell through
            // spreadsheet_format_ranges resets the destination to the workbook
            // default rather than leaving its previous colour in place.
            return string.Empty;
        }

        var c = color.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
