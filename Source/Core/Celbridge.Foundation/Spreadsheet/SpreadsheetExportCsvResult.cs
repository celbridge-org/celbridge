namespace Celbridge.Spreadsheet;

/// <summary>
/// Result of ISpreadsheetReader.ExportCsv. Csv is the RFC 4180 encoded text
/// and is empty when the requested range is empty. RowCount and ColumnCount
/// are the dimensions of the source range so callers can report a summary
/// without re-parsing the CSV (which would mis-count rows that contain
/// embedded line breaks).
/// </summary>
public record SpreadsheetExportCsvResult(string Csv, int RowCount, int ColumnCount);
