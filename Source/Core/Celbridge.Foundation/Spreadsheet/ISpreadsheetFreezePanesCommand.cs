using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by ISpreadsheetFreezePanesCommand on success. Sheet is the
/// worksheet name. Rows and Columns are the number of frozen rows and columns.
/// </summary>
public record SpreadsheetFreezePanesResult(
    string Sheet,
    int Rows,
    int Columns);

/// <summary>
/// Freezes the first N rows and first N columns of a worksheet so they remain
/// visible while the rest of the sheet scrolls. Either Rows or Columns may be
/// 0 to leave that axis unfrozen. Setting both to 0 clears any existing freeze.
/// </summary>
public interface ISpreadsheetFreezePanesCommand : IExecutableCommand<SpreadsheetFreezePanesResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet whose panes should be frozen.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// Number of rows from the top to freeze. 0 leaves rows unfrozen.
    /// </summary>
    int Rows { get; set; }

    /// <summary>
    /// Number of columns from the left to freeze. 0 leaves columns unfrozen.
    /// </summary>
    int Columns { get; set; }
}
