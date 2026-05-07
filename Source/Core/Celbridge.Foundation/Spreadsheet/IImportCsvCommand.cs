using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One CSV import in an IImportCsvCommand batch. Each entry
/// targets one sheet; CreateIfMissing is per-import so some entries can
/// require an existing sheet while others create new ones. InferTypes
/// turns plain integer / decimal / boolean fields into typed cell values
/// so downstream SUM, sorting and conditional formatting work; when false
/// every field is written as a string.
/// </summary>
public record CsvImport(
    string Sheet,
    string CsvText,
    bool CreateIfMissing = false,
    bool InferTypes = true);

/// <summary>
/// Result populated by IImportCsvCommand on success. ImportsApplied
/// is the number of CSV imports processed. TotalRowCount sums the row counts
/// across all imports. SheetsCreated is the count of imports that added a new
/// worksheet.
/// </summary>
public record ImportCsvResult(
    int ImportsApplied,
    int TotalRowCount,
    int SheetsCreated);

/// <summary>
/// Replaces the contents of one or more named sheets with parsed CSV data in a
/// single open/save cycle. The CSV is parsed per RFC 4180 (comma delimiter,
/// double-quote quoting, embedded quotes doubled, CRLF or LF line endings).
/// All rows in each CSV must have the same field count as that CSV's row 1.
/// Existing cells in each target sheet are cleared before the CSV block is
/// written. Other sheets in the workbook are untouched. Imports run in order;
/// if any import fails the whole batch fails and nothing is saved.
/// </summary>
public interface IImportCsvCommand : IExecutableCommand<ImportCsvResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// CSV imports to apply, in order.
    /// </summary>
    IReadOnlyList<CsvImport> Imports { get; set; }
}
