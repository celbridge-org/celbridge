using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by ISetActiveViewCommand on success. Sheet is the active
/// worksheet name. Range is the first range that was applied (or empty when
/// no selection was set). Ranges is the full selection that was applied (one
/// or more A1 ranges; empty when no selection was set). ActiveCell is the
/// anchor cell within the selection (or empty). TopLeftCell is the scroll
/// anchor that was applied (or empty).
/// </summary>
public record SetActiveViewResult(
    string Sheet,
    string Range,
    IReadOnlyList<string> Ranges,
    string ActiveCell,
    string TopLeftCell);

/// <summary>
/// Sets the persisted view state of an .xlsx workbook: which sheet is active
/// when the file is opened, plus optional cell selection and scroll position
/// on that sheet. The sheet is always made active. Ranges, Range, ActiveCell,
/// and TopLeftCell are optional; an empty value leaves that aspect unchanged.
/// </summary>
public interface ISetActiveViewCommand : IExecutableCommand<SetActiveViewResult>
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
    /// A1-notation cell or range to select on the target sheet. Empty string
    /// leaves the sheet's selection unchanged. When ActiveCell is also empty,
    /// the active cell defaults to the first cell of this range. Ignored
    /// when Ranges is non-empty.
    /// </summary>
    string Range { get; set; }

    /// <summary>
    /// A1-notation cells or ranges that together form a non-contiguous
    /// selection (the "Ctrl+click" selection in Excel). When non-empty, takes
    /// precedence over Range. Each entry must be a valid A1 cell or cell
    /// range without a sheet qualifier. Empty list defers to Range.
    /// </summary>
    IReadOnlyList<string> Ranges { get; set; }

    /// <summary>
    /// A1-notation single cell that becomes the active anchor cell within the
    /// selection. Empty string defers to Range / Ranges (active cell becomes
    /// the first cell of the first range). When set with no selection, the
    /// selection becomes just this single cell. When set together with a
    /// selection, ActiveCell must lie inside one of the ranges.
    /// </summary>
    string ActiveCell { get; set; }

    /// <summary>
    /// A1-notation single cell to anchor as the upper-left of the visible
    /// viewport on the target sheet. Empty string leaves the scroll position
    /// unchanged. Frozen panes may clamp this address.
    /// </summary>
    string TopLeftCell { get; set; }
}
