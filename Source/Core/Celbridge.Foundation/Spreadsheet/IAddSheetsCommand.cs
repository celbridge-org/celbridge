using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by IAddSheetsCommand on success. Sheets is the
/// list of sheet names that were added, in the order they were added.
/// </summary>
public record AddSheetsResult(IReadOnlyList<string> Sheets);

/// <summary>
/// Adds one or more empty worksheets to an .xlsx workbook in a single open/save
/// cycle. Sheets are appended after the existing sheets, in the order given.
/// Fails if any requested name collides with an existing sheet or with another
/// name in the same batch; in that case nothing is saved.
/// </summary>
public interface IAddSheetsCommand : IExecutableCommand<AddSheetsResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Names of the worksheets to add, in append order.
    /// </summary>
    IReadOnlyList<string> Sheets { get; set; }
}
