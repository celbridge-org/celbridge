namespace Celbridge.Spreadsheet;

/// <summary>
/// Workbook overview returned by ISpreadsheetReader.GetInfo. Lists every sheet
/// with its used range and dimensions, plus any defined named ranges.
/// </summary>
public record SpreadsheetWorkbookInfo(
    IReadOnlyList<SpreadsheetSheetInfo> Sheets,
    IReadOnlyList<SpreadsheetNamedRange> NamedRanges);

/// <summary>
/// A defined name in the workbook. Scope is either "workbook" or the name of
/// the worksheet that owns the name.
/// </summary>
public record SpreadsheetNamedRange(
    string Name,
    string RefersTo,
    string Scope);
