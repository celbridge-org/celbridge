using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One entry in a SpreadsheetDeleteCommand batch. Each operation targets a
/// specific sheet and a row range or column range. Cell ranges are not
/// accepted (Excel's "shift cells up/left" is a footgun and not exposed).
/// Range examples: "3" or "3:5" for rows; "B" or "B:D" for columns.
/// </summary>
public record SpreadsheetDeleteOperation(
    string Sheet,
    string Range);

/// <summary>
/// Result populated by ISpreadsheetDeleteCommand on success. OperationsApplied
/// is the number of operations processed. DeletedRowCount and
/// DeletedColumnCount are the totals across all operations after dedup.
/// </summary>
public record SpreadsheetDeleteResult(
    int OperationsApplied,
    int DeletedRowCount,
    int DeletedColumnCount);

/// <summary>
/// Deletes contiguous ranges of rows or columns from one or more sheets in a
/// single open/save cycle. Each operation references the original coordinate
/// space (the workbook as it was before this batch ran), so an agent can
/// specify "rows 3:5 and 10" without having to mentally shift indices. The
/// implementation collects the union of indices per sheet and per axis, then
/// deletes in descending order so earlier deletes do not shift later ones.
/// Rows below a deleted row range shift up; columns to the right of a deleted
/// column range shift left. Formulas are updated by the spreadsheet engine.
/// If any operation fails (bad range, missing sheet) the whole batch fails
/// and nothing is saved.
/// </summary>
public interface ISpreadsheetDeleteCommand : IExecutableCommand<SpreadsheetDeleteResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Delete operations to apply. Indices are interpreted against the
    /// original workbook state.
    /// </summary>
    IReadOnlyList<SpreadsheetDeleteOperation> Operations { get; set; }
}
