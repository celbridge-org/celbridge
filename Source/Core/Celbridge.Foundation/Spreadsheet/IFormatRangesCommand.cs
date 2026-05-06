using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One entry in an IFormatRangesCommand batch. Each edit targets a
/// specific sheet and range with its own format spec, so a single batch can
/// span multiple sheets in the same workbook.
/// </summary>
public record SpreadsheetFormatEdit(
    string Sheet,
    string Range,
    SpreadsheetFormatSpec Format);

/// <summary>
/// Result populated by IFormatRangesCommand on success.
/// EditsApplied is the number of edits processed. PropertiesApplied is the
/// total count of top-level SpreadsheetFormatSpec fields summed across edits.
/// AutoFitApplied is true when at least one edit triggered AdjustToContents.
/// </summary>
public record SpreadsheetFormatRangesResult(
    int EditsApplied,
    int PropertiesApplied,
    bool AutoFitApplied);

/// <summary>
/// Applies a batch of SpreadsheetFormatEdits to one workbook in a single
/// open/save cycle. Edits may target different sheets in the same workbook.
/// Each edit's format spec is applied independently; only its non-null fields
/// are set, and other formatting on the target cells is preserved. Edits run
/// in order; if any edit fails (bad colour, missing sheet, etc.) the whole
/// batch fails and nothing is saved. Formulas are recalculated as part of the
/// save once all edits succeed.
/// </summary>
public interface IFormatRangesCommand : IExecutableCommand<SpreadsheetFormatRangesResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Format edits to apply, in order.
    /// </summary>
    IReadOnlyList<SpreadsheetFormatEdit> Edits { get; set; }
}
