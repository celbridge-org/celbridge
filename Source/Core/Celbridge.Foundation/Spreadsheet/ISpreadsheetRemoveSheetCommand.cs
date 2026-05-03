using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Removes a worksheet from an .xlsx workbook. Fails if the sheet is not
/// found, or if it is the only sheet remaining (a workbook must contain at
/// least one sheet).
/// </summary>
public interface ISpreadsheetRemoveSheetCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to remove.
    /// </summary>
    string Sheet { get; set; }
}
