using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One entry in a SpreadsheetInsertCommand batch. Each operation targets a
/// specific sheet and a row range or column range. Cell ranges are not
/// accepted (Excel's "shift cells down/right" is a footgun and not exposed).
/// Range examples: "3" or "3:5" for rows; "B" or "B:D" for columns. The
/// width of the range determines how many empty rows or columns are inserted.
/// </summary>
public record SpreadsheetInsertOperation(
    string Sheet,
    string Range);

/// <summary>
/// Result populated by ISpreadsheetInsertCommand on success. OperationsApplied
/// is the number of operations processed. InsertedRowCount and
/// InsertedColumnCount are the totals across all operations after dedup.
/// </summary>
public record SpreadsheetInsertResult(
    int OperationsApplied,
    int InsertedRowCount,
    int InsertedColumnCount);

/// <summary>
/// Inserts empty rows or columns into one or more sheets in a single
/// open/save cycle. Each operation references the original coordinate space
/// (the workbook as it was before this batch ran), so an agent can specify
/// "insert rows at 3:5 and 10" without having to mentally shift indices. The
/// implementation collects the union of indices per sheet and per axis, then
/// inserts in descending order so earlier inserts do not shift later ones,
/// and overlapping ranges dedupe naturally. Existing rows at or below the
/// insert position shift down; existing columns at or to the right of the
/// insert position shift right. Formulas are updated by the spreadsheet
/// engine. If any operation fails (bad range, missing sheet) the whole batch
/// fails and nothing is saved.
/// </summary>
public interface ISpreadsheetInsertCommand : IExecutableCommand<SpreadsheetInsertResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Insert operations to apply. Indices are interpreted against the
    /// original workbook state.
    /// </summary>
    IReadOnlyList<SpreadsheetInsertOperation> Operations { get; set; }
}
