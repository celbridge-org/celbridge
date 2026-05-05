using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// A single cell write applied by ISpreadsheetWriteCellsCommand. Cell is an A1
/// address (e.g. "B3"). Value is the JSON-typed value to write: a JSON null
/// blanks the cell, numbers and booleans round-trip directly, strings are
/// written as text. Set IsFormula true to write the string as a formula
/// (the leading '=' is optional). Explicit beats sniffing — strings that
/// happen to start with '=' are written as text unless IsFormula is true.
/// </summary>
public record SpreadsheetCellEdit(
    string Cell,
    object? Value,
    bool IsFormula = false);

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
