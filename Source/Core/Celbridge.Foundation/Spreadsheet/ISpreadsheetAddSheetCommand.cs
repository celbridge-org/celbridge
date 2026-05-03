using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Adds a new empty worksheet to an .xlsx workbook. The sheet is appended
/// after the existing sheets. Fails if a worksheet with the same name already
/// exists.
/// </summary>
public interface ISpreadsheetAddSheetCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to add.
    /// </summary>
    string Sheet { get; set; }
}
