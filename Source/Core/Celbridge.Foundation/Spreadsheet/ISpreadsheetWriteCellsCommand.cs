using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Applies a batch of single-cell edits to an .xlsx workbook. The workbook is
/// opened, mutated, and saved back to disk. Presentation on cells the edits do
/// not touch is preserved. Formulas are recalculated as part of the save so
/// readers see fresh cached values.
/// </summary>
public interface ISpreadsheetWriteCellsCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to write into. The sheet must already exist;
    /// callers create it via ISpreadsheetAddSheetCommand first if needed.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// Cell edits to apply, in the order supplied. Later edits to the same
    /// cell overwrite earlier ones.
    /// </summary>
    IReadOnlyList<SpreadsheetCellEdit> Edits { get; set; }
}
