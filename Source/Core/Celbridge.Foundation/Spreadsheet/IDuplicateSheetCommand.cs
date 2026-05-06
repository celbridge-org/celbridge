using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by IDuplicateSheetCommand on success. NewSheet
/// is the name the duplicate was added under, Position is its 1-based tab
/// position in the workbook after the operation.
/// </summary>
public record SpreadsheetDuplicateSheetResult(string NewSheet, int Position);

/// <summary>
/// Duplicates an existing worksheet in an .xlsx workbook. The copy preserves
/// values, formulas, formatting, conditional formatting, freeze panes, column
/// widths, row heights, and any other sheet-level state. Fails if the source
/// sheet does not exist, the new name collides with an existing sheet, or the
/// position is outside [0, sheetCount + 1] (0 appends after existing sheets).
/// </summary>
public interface IDuplicateSheetCommand : IExecutableCommand<SpreadsheetDuplicateSheetResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to duplicate.
    /// </summary>
    string SourceSheet { get; set; }

    /// <summary>
    /// Name to give the duplicate. Must not collide with an existing sheet.
    /// </summary>
    string NewSheet { get; set; }

    /// <summary>
    /// 1-based tab position to place the duplicate at, or 0 to append it after
    /// the existing sheets.
    /// </summary>
    int Position { get; set; }
}
