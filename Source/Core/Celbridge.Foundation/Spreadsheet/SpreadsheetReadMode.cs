namespace Celbridge.Spreadsheet;

/// <summary>
/// Selects whether ISpreadsheetReader.ReadSheet returns computed cell values or
/// the underlying formula text for cells that contain a formula.
/// </summary>
public enum SpreadsheetReadMode
{
    /// <summary>
    /// Return the cached computed value for each cell. Cells without a formula
    /// return their literal value.
    /// </summary>
    Values,

    /// <summary>
    /// Return the formula text (with the leading '=') for cells that contain a
    /// formula. Cells without a formula return their literal value.
    /// </summary>
    Formulas
}
