namespace Celbridge.Spreadsheet;

/// <summary>
/// Result of ISpreadsheetReader.ReadSheet. Rows is either a list of row arrays
/// (when Headers was false) or a list of row dictionaries keyed by header
/// (when Headers was true). Both shapes are JSON-serialised via object?.
/// TotalRowCount is the total number of data rows the requested range would
/// produce ignoring offset and limit, so the caller can decide whether to page.
/// Headers carries the resolved header names when Headers was requested,
/// otherwise it is empty.
/// </summary>
public record SpreadsheetReadResult(
    IReadOnlyList<object?> Rows,
    int TotalRowCount,
    IReadOnlyList<string> Headers);
