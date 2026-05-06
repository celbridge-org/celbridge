using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Moves an existing worksheet to a new 1-based tab position in an .xlsx workbook.
/// Fails if the sheet is not found or the position is outside [1, sheetCount].
/// </summary>
public interface IMoveSheetCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to move.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// 1-based tab position to move the sheet to.
    /// </summary>
    int Position { get; set; }
}
