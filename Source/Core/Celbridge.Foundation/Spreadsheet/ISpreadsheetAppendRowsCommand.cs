using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by ISpreadsheetAppendRowsCommand on success. FirstRow and
/// LastRow are the 1-based row numbers that the appended block now occupies.
/// AppendedRowCount equals LastRow - FirstRow + 1.
/// </summary>
public record SpreadsheetAppendRowsResult(
    int AppendedRowCount,
    int FirstRow,
    int LastRow);

/// <summary>
/// Appends row arrays to the end of a worksheet's used range. An empty sheet
/// receives the rows starting at A1. Each inner list maps positionally to
/// columns A, B, C, .... Missing trailing values leave cells blank. Formula
/// writes (any cell value beginning with '=') are interpreted as text. Use
/// ISpreadsheetWriteCellsCommand for formula writes.
/// </summary>
public interface ISpreadsheetAppendRowsCommand : IExecutableCommand<SpreadsheetAppendRowsResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to append to. The sheet must already exist;
    /// callers create it via ISpreadsheetAddSheetCommand first if needed.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// Rows to append. Outer list is rows, inner list is the cell values for
    /// that row in column order starting from column A.
    /// </summary>
    IReadOnlyList<IReadOnlyList<object?>> Rows { get; set; }
}
