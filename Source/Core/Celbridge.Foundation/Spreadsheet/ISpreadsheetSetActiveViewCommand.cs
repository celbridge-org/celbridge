using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Sets the persisted view state of an .xlsx workbook: which sheet is active
/// when the file is opened, plus optional cell selection and scroll position
/// on that sheet. The sheet is always made active. Range and TopLeftCell are
/// optional; an empty value leaves that aspect of the sheet's view unchanged.
/// </summary>
public interface ISpreadsheetSetActiveViewCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to make active.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// A1-notation cell or range to select on the target sheet. The active
    /// cell becomes its top-left. Empty string leaves the sheet's selection
    /// unchanged.
    /// </summary>
    string Range { get; set; }

    /// <summary>
    /// A1-notation single cell to anchor as the upper-left of the visible
    /// viewport on the target sheet. Empty string leaves the scroll position
    /// unchanged. Frozen panes may clamp this address.
    /// </summary>
    string TopLeftCell { get; set; }
}
