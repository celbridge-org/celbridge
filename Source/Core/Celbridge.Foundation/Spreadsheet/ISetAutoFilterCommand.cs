using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// Result populated by ISetAutoFilterCommand on success. Enabled
/// reflects whether the sheet has an active auto-filter after the operation.
/// FilterRange is the A1 range the filter covers when Enabled is true, or
/// the empty string when the filter was cleared.
/// </summary>
public record SetAutoFilterResult(bool Enabled, string FilterRange);

/// <summary>
/// Sets or clears the auto-filter on a single worksheet in an .xlsx workbook.
/// When Enabled is true, the auto-filter is applied to Range (or the
/// worksheet's used range when Range is empty). When Enabled is false, any
/// existing auto-filter on the sheet is cleared and Range is ignored. Each
/// worksheet supports at most one auto-filter; setting a new one replaces any
/// existing filter on the sheet.
/// </summary>
public interface ISetAutoFilterCommand : IExecutableCommand<SetAutoFilterResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to set the auto-filter on.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// A1 cell range to apply the filter to. Empty string applies the filter
    /// to the worksheet's entire used range. Ignored when Enabled is false.
    /// Only A1 cell ranges are accepted (e.g. "A1:F100"); column-letter and
    /// row-number ranges are rejected.
    /// </summary>
    string Range { get; set; }

    /// <summary>
    /// True to apply an auto-filter, false to clear any existing filter.
    /// </summary>
    bool Enabled { get; set; }
}
