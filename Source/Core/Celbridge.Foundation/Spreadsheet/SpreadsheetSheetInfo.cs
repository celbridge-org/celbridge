namespace Celbridge.Spreadsheet;

/// <summary>
/// Summary metadata for a single worksheet inside an .xlsx workbook.
/// Position is the 1-based tab position. UsedRange is the A1-notation range
/// that bounds all non-empty cells, or null if the sheet has no used range.
/// </summary>
public record SpreadsheetSheetInfo(
    string Name,
    int Position,
    string? UsedRange,
    int RowCount,
    int ColumnCount);
