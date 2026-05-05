using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One entry in a SpreadsheetClearCommand batch. Each operation targets a
/// specific sheet and range. Range may be any A1 form: cell range
/// ("A1:C3"), single cell ("B2"), column or column range ("E", "B:D"),
/// row or row range ("3", "3:5"), or empty string to clear the entire
/// sheet.
/// </summary>
public record SpreadsheetClearOperation(
    string Sheet,
    string Range);

/// <summary>
/// Result populated by ISpreadsheetClearCommand on success. OperationsApplied
/// is the number of operations processed. CellCount is the total number of
/// cells whose state was reset across all operations: cells carrying values,
/// formulas, formatting, comments, merged-range membership or data validation
/// all increment the count. Cells in the targeted range that were already
/// fully default do not. Overlap between operations is counted once per
/// operation.
/// </summary>
public record SpreadsheetClearResult(
    int OperationsApplied,
    int CellCount);

/// <summary>
/// Clears cell content, formatting, comments, merged ranges, and data
/// validation in a batch of ranges across one or more sheets in a single
/// open/save cycle. Unlike Delete, Clear does not shift remaining cells —
/// the cleared range is simply emptied in place. Sheet identity (tab name,
/// position, color, frozen panes, named ranges, column widths, row heights)
/// is preserved when an entire sheet is cleared. If any operation fails
/// (bad range, missing sheet) the whole batch fails and nothing is saved.
/// </summary>
public interface ISpreadsheetClearCommand : IExecutableCommand<SpreadsheetClearResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Clear operations to apply, in order.
    /// </summary>
    IReadOnlyList<SpreadsheetClearOperation> Operations { get; set; }
}
