using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One key in an ISortRangeCommand sort. Column is the absolute A1
/// column letter (e.g. "B") or 1-based column number; it must lie within
/// the range being sorted. Ascending controls the order for this key.
/// </summary>
public record SpreadsheetSortKey(
    string Column,
    bool Ascending);

/// <summary>
/// Result populated by ISortRangeCommand on success. RowCount is the
/// number of rows that were re-ordered (excludes the header row when
/// HasHeaderRow is true).
/// </summary>
public record SpreadsheetSortRangeResult(
    int RowCount);

/// <summary>
/// Sorts the rows of a cell range by one or more columns, in order. The
/// columns are absolute references (sheet letters/numbers); they are
/// translated to range-relative references for the spreadsheet engine. When
/// HasHeaderRow is true, the first row of the range stays in place and only
/// the rows below it are sorted.
/// </summary>
public interface ISortRangeCommand : IExecutableCommand<SpreadsheetSortRangeResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet whose range should be sorted. Required.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// A1 cell range to sort (e.g. "A2:F100"). Empty string sorts the
    /// worksheet's entire used range.
    /// </summary>
    string Range { get; set; }

    /// <summary>
    /// Sort keys, applied in order. Must contain at least one key.
    /// </summary>
    IReadOnlyList<SpreadsheetSortKey> SortKeys { get; set; }

    /// <summary>
    /// When true, the first row of the range is treated as a header and is
    /// excluded from the sort.
    /// </summary>
    bool HasHeaderRow { get; set; }

    /// <summary>
    /// When true, text comparisons are case-sensitive. Excel's default is
    /// false (case-insensitive).
    /// </summary>
    bool MatchCase { get; set; }
}
