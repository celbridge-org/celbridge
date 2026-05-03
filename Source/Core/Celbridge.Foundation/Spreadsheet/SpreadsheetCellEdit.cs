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
