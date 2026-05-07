using Celbridge.Commands;

namespace Celbridge.Spreadsheet;

/// <summary>
/// One conditional formatting rule for ISetConditionalFormattingCommand.
///
/// Type names (case-insensitive): "greaterThan", "greaterThanOrEqual",
/// "lessThan", "lessThanOrEqual", "equal", "notEqual", "between", "notBetween",
/// "containsText", "doesNotContainText", "beginsWith", "endsWith", "isBlank",
/// "isNotBlank", "isError", "isNotError", "duplicateValues", "uniqueValues",
/// "formula", "top", "bottom", "topPercent", "bottomPercent",
/// "colorScale2", "colorScale3".
///
/// Numeric comparisons use Value (and Value2 for between/notBetween). Text
/// predicates use Text. Formula rules use Formula (an Excel formula string,
/// with or without a leading "="). Top/bottom rules use Value as the integer
/// count (items for "top"/"bottom"; percent 1-100 for "topPercent"/"bottomPercent").
/// Color scales use LowColor / HighColor (and MidColor for colorScale3); each
/// stop's threshold is controlled by the corresponding *Type / *Value pair
/// (see below). Other formatting fields are ignored for color scales. All
/// non-color-scale rule types may set BackgroundColor, FontColor, Bold and
/// Italic to drive the matched-cell formatting.
///
/// Color-scale stop types (case-insensitive):
///   LowType: "min" (default - lowest value in range), "number", "percent",
///     "percentile", "formula".
///   MidType (colorScale3 only): "percent" (default at value "50"),
///     "number", "percentile", "formula".
///   HighType: "max" (default - highest value in range), "number", "percent",
///     "percentile", "formula".
/// When a stop type other than "min"/"max"/default is used, the corresponding
/// *Value is required; "number"/"percent"/"percentile" parse as a number,
/// "formula" is an Excel formula with or without a leading "=".
///
/// Colors are CSS hex strings (#RRGGBB).
/// </summary>
public record ConditionalFormatRule(
    string Type,
    double? Value = null,
    double? Value2 = null,
    string? Text = null,
    string? Formula = null,
    string? BackgroundColor = null,
    string? FontColor = null,
    bool? Bold = null,
    bool? Italic = null,
    string? LowColor = null,
    string? MidColor = null,
    string? HighColor = null,
    string? LowType = null,
    string? LowValue = null,
    string? MidType = null,
    string? MidValue = null,
    string? HighType = null,
    string? HighValue = null);

/// <summary>
/// Result populated by ISetConditionalFormattingCommand on success.
/// RulesApplied is the number of rules added to the target range. RulesRemoved
/// is the number of pre-existing rules that were removed before adding the new
/// ones (always 0 unless ClearExisting was true).
/// </summary>
public record SetConditionalFormattingResult(int RulesApplied, int RulesRemoved);

/// <summary>
/// Adds one or more conditional formatting rules to an A1 cell range on a
/// worksheet. Optionally clears any pre-existing rules whose ranges intersect
/// the target range first. Common cases: highlight cells above or below a
/// threshold, colour scale across a column of numbers, formula-based highlight
/// for whole rows.
/// </summary>
public interface ISetConditionalFormattingCommand : IExecutableCommand<SetConditionalFormattingResult>
{
    /// <summary>
    /// Resource key of the .xlsx workbook to mutate.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Name of the worksheet to apply the rules to.
    /// </summary>
    string Sheet { get; set; }

    /// <summary>
    /// A1 cell range to apply the rules to (e.g. "A1:F100" or "B2"). Column-
    /// letter and row-number ranges are rejected.
    /// </summary>
    string Range { get; set; }

    /// <summary>
    /// Rules to add to the target range, in priority order (earlier rules win
    /// over later rules when stopIfTrue semantics differ across them).
    /// </summary>
    IReadOnlyList<ConditionalFormatRule> Rules { get; set; }

    /// <summary>
    /// When true, removes any pre-existing conditional formatting rules whose
    /// ranges intersect the target range before adding the new rules. When
    /// false (default), pre-existing rules are left in place.
    /// </summary>
    bool ClearExisting { get; set; }
}
