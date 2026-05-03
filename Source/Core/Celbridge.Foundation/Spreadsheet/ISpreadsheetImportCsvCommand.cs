using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by ISpreadsheetImportCsvCommand on success. RowCount and
/// ColumnCount are the dimensions of the imported block, starting at A1.
/// SheetCreated is true when a new worksheet was added by this call.
/// </summary>
public record SpreadsheetImportCsvResult(
    int RowCount,
    int ColumnCount,
    bool SheetCreated);

/// <summary>
/// Replaces the contents of a named sheet with parsed CSV data. The CSV is
/// parsed per RFC 4180 (comma delimiter, double-quote quoting, embedded quotes
/// doubled, CRLF or LF line endings). When the sheet does not exist and
/// CreateIfMissing is true, the sheet is created; otherwise the call fails.
/// Existing cells in the sheet are cleared before the CSV block is written.
/// Other sheets in the workbook are untouched.
/// </summary>
public interface ISpreadsheetImportCsvCommand : IExecutableCommand<SpreadsheetImportCsvResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to populate.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// CSV text to parse and write into the sheet.
    /// </summary>
    string CsvText { get; set; }

    /// <summary>
    /// When true, create the sheet if it does not exist. When false, the
    /// command fails on a missing sheet.
    /// </summary>
    bool CreateIfMissing { get; set; }
}
