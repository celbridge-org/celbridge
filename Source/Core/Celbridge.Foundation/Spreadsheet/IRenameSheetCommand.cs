using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by IRenameSheetCommand on success. PreviousName is the
/// sheet's name before the rename and NewName is its name after.
/// </summary>
public record RenameSheetResult(string PreviousName, string NewName);

/// <summary>
/// Renames an existing worksheet in an .xlsx workbook. Fails if the source
/// sheet is not found, or if the new name collides with another sheet.
/// </summary>
public interface IRenameSheetCommand : IExecutableCommand<RenameSheetResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Current name of the worksheet to rename.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// New name to assign to the worksheet.
    /// </summary>
    string NewName { get; set; }
}
