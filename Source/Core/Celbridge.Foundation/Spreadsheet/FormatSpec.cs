namespace Celbridge.Spreadsheet;

/// <summary>
/// Per-side border specification for BordersSpec. Style is a
/// border-style key (SOLID, DASHED, DOTTED, DOUBLE, NONE, or a ClosedXML
/// XLBorderStyleValues name). Color is a CSS hex string (#RRGGBB), or
/// the empty string to reset the border colour to the workbook default.
/// Null fields are left unchanged.
/// </summary>
public record BorderSide(
    string? Style = null,
    string? Color = null);

/// <summary>
/// Per-side border configuration for FormatSpec. Null sides are
/// left unchanged.
/// </summary>
public record BordersSpec(
    BorderSide? Top = null,
    BorderSide? Bottom = null,
    BorderSide? Left = null,
    BorderSide? Right = null);

/// <summary>
/// Font and text styling for FormatSpec. Null fields are left
/// unchanged. ForegroundColor and FontFamily accept the empty string as a
/// reset sentinel (font colour or family is restored to the workbook
/// default). FontSize accepts a non-positive value (zero or negative) as a
/// reset sentinel; otherwise the value is applied verbatim and is in points.
/// ForegroundColor is a CSS hex string (#RRGGBB).
/// </summary>
public record TextFormat(
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    bool? Strikethrough = null,
    string? FontFamily = null,
    double? FontSize = null,
    string? ForegroundColor = null);

/// <summary>
/// Style specification for spreadsheet_format_ranges. Follows the Google Sheets
/// API CellFormat shape so agents have a familiar naming convention. Only
/// fields that are present in the JSON object are applied; other formatting on
/// the target cells is preserved.
///
/// Colours are CSS hex strings (#RRGGBB), or the empty string to clear the
/// fill / restore the colour to the workbook default. HorizontalAlignment
/// values: LEFT, CENTER, RIGHT, GENERAL, JUSTIFY. VerticalAlignment values:
/// TOP, MIDDLE, BOTTOM. ColumnWidth is in Excel character units (NOT pixels):
/// default is 8.43, typical column is 10 to 60, values above 100 are almost
/// always a mistake; a negative value resets to the workbook default.
/// RowHeight is in points: default is 15, typical row is 12 to 30; a negative
/// value resets to the workbook default. AutoFitColumns calls
/// AdjustToContents() after any explicit ColumnWidth and is usually preferable
/// to guessing a width. MergeRange = true merges the range; MergeRange = false
/// unmerges any existing merge that covers the range. When read back via
/// spreadsheet_read_format, MergeRange is true for cells that are part of a
/// merged range and absent otherwise.
/// </summary>
public record FormatSpec(
    TextFormat? TextFormat = null,
    string? BackgroundColor = null,
    BordersSpec? Borders = null,
    string? HorizontalAlignment = null,
    string? VerticalAlignment = null,
    bool? WrapText = null,
    string? NumberFormat = null,
    double? ColumnWidth = null,
    double? RowHeight = null,
    bool? AutoFitColumns = null,
    bool? MergeRange = null);
