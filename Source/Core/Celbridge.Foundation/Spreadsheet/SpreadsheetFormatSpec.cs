namespace Celbridge.Spreadsheet;

/// <summary>
/// Per-side border specification for SpreadsheetBordersSpec. Style is a
/// border-style key (SOLID, DASHED, DOTTED, DOUBLE, NONE, or a ClosedXML
/// XLBorderStyleValues name). Color is a CSS hex string (#RRGGBB), or
/// the empty string to reset the border colour to the workbook default.
/// Null fields are left unchanged.
/// </summary>
public record SpreadsheetBorderSide(
    string? Style = null,
    string? Color = null);

/// <summary>
/// Per-side border configuration for SpreadsheetFormatSpec. Null sides are
/// left unchanged.
/// </summary>
public record SpreadsheetBordersSpec(
    SpreadsheetBorderSide? Top = null,
    SpreadsheetBorderSide? Bottom = null,
    SpreadsheetBorderSide? Left = null,
    SpreadsheetBorderSide? Right = null);

/// <summary>
/// Font and text styling for SpreadsheetFormatSpec. Null fields are left
/// unchanged. ForegroundColor and FontFamily accept the empty string as a
/// reset sentinel (font colour or family is restored to the workbook
/// default). FontSize accepts a non-positive value (zero or negative) as a
/// reset sentinel; otherwise the value is applied verbatim and is in points.
/// ForegroundColor is a CSS hex string (#RRGGBB).
/// </summary>
public record SpreadsheetTextFormat(
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
/// unmerges any existing merge that covers the range.
/// </summary>
public record SpreadsheetFormatSpec(
    SpreadsheetTextFormat? TextFormat = null,
    string? BackgroundColor = null,
    SpreadsheetBordersSpec? Borders = null,
    string? HorizontalAlignment = null,
    string? VerticalAlignment = null,
    bool? WrapText = null,
    string? NumberFormat = null,
    double? ColumnWidth = null,
    double? RowHeight = null,
    bool? AutoFitColumns = null,
    bool? MergeRange = null);
