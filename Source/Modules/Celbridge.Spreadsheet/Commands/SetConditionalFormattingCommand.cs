using System.Globalization;
using Celbridge.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class SetConditionalFormattingCommand : CommandBase, ISetConditionalFormattingCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public IReadOnlyList<ConditionalFormatRule> Rules { get; set; } = Array.Empty<ConditionalFormatRule>();
    public bool ClearExisting { get; set; }

    public SetConditionalFormattingResult ResultValue { get; private set; } =
        new SetConditionalFormattingResult(0, 0);

    public SetConditionalFormattingCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resolveResult = SpreadsheetHelper.ResolveWorkbookPath(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }

        if (string.IsNullOrEmpty(Range))
        {
            return Result.Fail("Range is required.");
        }

        if (SpreadsheetHelper.IsColumnRange(Range) || SpreadsheetHelper.IsRowRange(Range))
        {
            return Result.Fail($"Conditional formatting range must be an A1 cell range, was '{Range}'.");
        }

        if (Rules.Count == 0 && !ClearExisting)
        {
            return Result.Fail("At least one rule is required when clearExisting is false.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            IXLRange targetRange;
            try
            {
                targetRange = worksheet.Range(Range);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Invalid cell range '{Range}': {ex.Message}");
            }

            int rulesRemoved = 0;
            if (ClearExisting)
            {
                var formatsToRemove = new HashSet<IXLConditionalFormat>();
                foreach (var existing in worksheet.ConditionalFormats)
                {
                    if (existing.Range.RangeAddress.Intersects(targetRange.RangeAddress))
                    {
                        formatsToRemove.Add(existing);
                    }
                }
                rulesRemoved = formatsToRemove.Count;
                worksheet.ConditionalFormats.Remove(format => formatsToRemove.Contains(format));
            }

            for (int ruleIndex = 0; ruleIndex < Rules.Count; ruleIndex++)
            {
                var rule = Rules[ruleIndex];
                var applyResult = ApplyRule(targetRange, rule);
                if (applyResult.IsFailure)
                {
                    return Result.Fail($"Rule {ruleIndex + 1}: {applyResult.FirstErrorMessage}");
                }
            }

            SpreadsheetHelper.RecalculateAndSave(workbook);

            ResultValue = new SetConditionalFormattingResult(Rules.Count, rulesRemoved);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set conditional formatting on '{Sheet}!{Range}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    private static Result ApplyRule(IXLRange targetRange, ConditionalFormatRule rule)
    {
        if (string.IsNullOrEmpty(rule.Type))
        {
            return Result.Fail("Rule type is required.");
        }

        var conditionalFormat = targetRange.AddConditionalFormat();
        var typeKey = rule.Type.ToLowerInvariant();

        if (typeKey == "colorscale2")
        {
            return ApplyColorScale2(conditionalFormat, rule);
        }
        if (typeKey == "colorscale3")
        {
            return ApplyColorScale3(conditionalFormat, rule);
        }

        var styleResult = ApplyComparisonRule(conditionalFormat, typeKey, rule);
        if (styleResult.IsFailure)
        {
            return styleResult;
        }
        var style = styleResult.Value;

        var formattingResult = ApplyRuleFormatting(style, rule);
        if (formattingResult.IsFailure)
        {
            return formattingResult;
        }

        return Result.Ok();
    }

    private static Result<IXLStyle> ApplyComparisonRule(IXLConditionalFormat conditionalFormat, string typeKey, ConditionalFormatRule rule)
    {
        IXLStyle style;
        switch (typeKey)
        {
            case "greaterthan":
                if (!rule.Value.HasValue) return Result.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenGreaterThan(rule.Value.Value);
                break;
            case "greaterthanorequal":
                if (!rule.Value.HasValue) return Result.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenEqualOrGreaterThan(rule.Value.Value);
                break;
            case "lessthan":
                if (!rule.Value.HasValue) return Result.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenLessThan(rule.Value.Value);
                break;
            case "lessthanorequal":
                if (!rule.Value.HasValue) return Result.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenEqualOrLessThan(rule.Value.Value);
                break;
            case "equal":
                if (!rule.Value.HasValue) return Result.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenEquals(rule.Value.Value);
                break;
            case "notequal":
                if (!rule.Value.HasValue) return Result.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenNotEquals(rule.Value.Value);
                break;
            case "between":
                if (!rule.Value.HasValue || !rule.Value2.HasValue)
                    return Result.Fail($"'{rule.Type}' rule requires both 'value' and 'value2'.");
                style = conditionalFormat.WhenBetween(rule.Value.Value, rule.Value2.Value);
                break;
            case "notbetween":
                if (!rule.Value.HasValue || !rule.Value2.HasValue)
                    return Result.Fail($"'{rule.Type}' rule requires both 'value' and 'value2'.");
                style = conditionalFormat.WhenNotBetween(rule.Value.Value, rule.Value2.Value);
                break;
            case "containstext":
                if (string.IsNullOrEmpty(rule.Text)) return Result.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenContains(rule.Text);
                break;
            case "doesnotcontaintext":
                if (string.IsNullOrEmpty(rule.Text)) return Result.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenNotContains(rule.Text);
                break;
            case "beginswith":
                if (string.IsNullOrEmpty(rule.Text)) return Result.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenStartsWith(rule.Text);
                break;
            case "endswith":
                if (string.IsNullOrEmpty(rule.Text)) return Result.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenEndsWith(rule.Text);
                break;
            case "isblank":
                style = conditionalFormat.WhenIsBlank();
                break;
            case "isnotblank":
                style = conditionalFormat.WhenNotBlank();
                break;
            case "iserror":
                style = conditionalFormat.WhenIsError();
                break;
            case "isnoterror":
                style = conditionalFormat.WhenNotError();
                break;
            case "duplicatevalues":
                style = conditionalFormat.WhenIsDuplicate();
                break;
            case "uniquevalues":
                style = conditionalFormat.WhenIsUnique();
                break;
            case "formula":
                if (string.IsNullOrEmpty(rule.Formula)) return Result.Fail($"'{rule.Type}' rule requires 'formula'.");
                var formula = rule.Formula.StartsWith('=') ? rule.Formula.Substring(1) : rule.Formula;
                style = conditionalFormat.WhenIsTrue(formula);
                break;
            case "top":
            case "bottom":
            case "toppercent":
            case "bottompercent":
                var topBottomResult = ApplyTopBottomRule(conditionalFormat, typeKey, rule);
                if (topBottomResult.IsFailure) return Result.Fail(topBottomResult.FirstErrorMessage);
                style = topBottomResult.Value;
                break;
            default:
                // Catch the most common confusion: the agent picks `colorScale`
                // because every other rule reads as a single noun. The arity is
                // baked into the type name here; surface a hint so they don't
                // have to read the troubleshooter.
                if (string.Equals(rule.Type, "colorScale", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Fail(
                        $"Unknown rule type: '{rule.Type}'. Did you mean 'colorScale2' (low + high) or 'colorScale3' (low + mid + high)?");
                }
                return Result.Fail($"Unknown rule type: '{rule.Type}'.");
        }

        return style.OkResult();
    }

    private static Result ApplyRuleFormatting(IXLStyle style, ConditionalFormatRule rule)
    {
        if (rule.BackgroundColor is not null)
        {
            var colorResult = FormatConverterHelper.ParseColor(rule.BackgroundColor);
            if (colorResult.IsFailure)
            {
                return colorResult;
            }
            style.Fill.PatternType = XLFillPatternValues.Solid;
            style.Fill.BackgroundColor = colorResult.Value;
        }

        if (rule.FontColor is not null)
        {
            var colorResult = FormatConverterHelper.ParseColor(rule.FontColor);
            if (colorResult.IsFailure)
            {
                return colorResult;
            }
            style.Font.FontColor = colorResult.Value;
        }

        if (rule.Bold.HasValue)
        {
            style.Font.Bold = rule.Bold.Value;
        }

        if (rule.Italic.HasValue)
        {
            style.Font.Italic = rule.Italic.Value;
        }

        return Result.Ok();
    }

    private static Result<IXLStyle> ApplyTopBottomRule(IXLConditionalFormat conditionalFormat, string typeKey, ConditionalFormatRule rule)
    {
        if (!rule.Value.HasValue)
        {
            return Result.Fail($"'{rule.Type}' rule requires a numeric 'value' (the count or percent).");
        }

        var rawValue = rule.Value.Value;
        if (rawValue < 1 || rawValue > int.MaxValue || Math.Floor(rawValue) != rawValue)
        {
            return Result.Fail($"'{rule.Type}' rule 'value' must be a positive integer, was {rawValue}.");
        }
        var count = (int)rawValue;

        var isPercent = typeKey == "toppercent" || typeKey == "bottompercent";
        if (isPercent && (count < 1 || count > 100))
        {
            return Result.Fail($"'{rule.Type}' rule 'value' must be between 1 and 100 when expressing a percent.");
        }

        var topBottomType = isPercent ? XLTopBottomType.Percent : XLTopBottomType.Items;
        var isTop = typeKey == "top" || typeKey == "toppercent";

        var style = isTop
            ? conditionalFormat.WhenIsTop(count, topBottomType)
            : conditionalFormat.WhenIsBottom(count, topBottomType);

        return style.OkResult();
    }

    private static Result ApplyColorScale2(IXLConditionalFormat conditionalFormat, ConditionalFormatRule rule)
    {
        var lowColorResult = FormatConverterHelper.ParseColor(rule.LowColor ?? "#FFFFFF");
        if (lowColorResult.IsFailure)
        {
            return lowColorResult;
        }
        var highColorResult = FormatConverterHelper.ParseColor(rule.HighColor ?? "#FF0000");
        if (highColorResult.IsFailure)
        {
            return highColorResult;
        }

        var builder = conditionalFormat.ColorScale();

        var lowStop = ApplyLowStop(builder, rule, lowColorResult.Value);
        if (lowStop.IsFailure)
        {
            return lowStop;
        }

        return ApplyHighStopTwoStop(lowStop.Value, rule, highColorResult.Value);
    }

    private static Result ApplyColorScale3(IXLConditionalFormat conditionalFormat, ConditionalFormatRule rule)
    {
        var lowColorResult = FormatConverterHelper.ParseColor(rule.LowColor ?? "#FF0000");
        if (lowColorResult.IsFailure)
        {
            return lowColorResult;
        }
        var midColorResult = FormatConverterHelper.ParseColor(rule.MidColor ?? "#FFFFFF");
        if (midColorResult.IsFailure)
        {
            return midColorResult;
        }
        var highColorResult = FormatConverterHelper.ParseColor(rule.HighColor ?? "#00FF00");
        if (highColorResult.IsFailure)
        {
            return highColorResult;
        }

        var builder = conditionalFormat.ColorScale();

        var lowStop = ApplyLowStop(builder, rule, lowColorResult.Value);
        if (lowStop.IsFailure)
        {
            return lowStop;
        }

        var midStop = ApplyMidStop(lowStop.Value, rule, midColorResult.Value);
        if (midStop.IsFailure)
        {
            return midStop;
        }

        return ApplyHighStopThreeStop(midStop.Value, rule, highColorResult.Value);
    }

    private static Result<IXLCFColorScaleMid> ApplyLowStop(IXLCFColorScaleMin builder, ConditionalFormatRule rule, XLColor lowColor)
    {
        if (string.IsNullOrEmpty(rule.LowType) || string.Equals(rule.LowType, "min", StringComparison.OrdinalIgnoreCase))
        {
            return builder.LowestValue(lowColor).OkResult();
        }

        var stopResult = ParseColorScaleStop("low", rule.LowType!, rule.LowValue);
        if (stopResult.IsFailure)
        {
            return Result.Fail(stopResult.FirstErrorMessage);
        }
        var stop = stopResult.Value;

        var afterLow = stop.IsFormula
            ? builder.Minimum(stop.ContentType, stop.FormulaValue, lowColor)
            : builder.Minimum(stop.ContentType, stop.NumericValue, lowColor);
        return afterLow.OkResult();
    }

    private static Result<IXLCFColorScaleMax> ApplyMidStop(IXLCFColorScaleMid builder, ConditionalFormatRule rule, XLColor midColor)
    {
        if (string.IsNullOrEmpty(rule.MidType))
        {
            return builder.Midpoint(XLCFContentType.Percent, 50d, midColor).OkResult();
        }

        var stopResult = ParseColorScaleStop("mid", rule.MidType!, rule.MidValue);
        if (stopResult.IsFailure)
        {
            return Result.Fail(stopResult.FirstErrorMessage);
        }
        var stop = stopResult.Value;

        var afterMid = stop.IsFormula
            ? builder.Midpoint(stop.ContentType, stop.FormulaValue, midColor)
            : builder.Midpoint(stop.ContentType, stop.NumericValue, midColor);
        return afterMid.OkResult();
    }

    private static Result ApplyHighStopTwoStop(IXLCFColorScaleMid builder, ConditionalFormatRule rule, XLColor highColor)
    {
        if (string.IsNullOrEmpty(rule.HighType) || string.Equals(rule.HighType, "max", StringComparison.OrdinalIgnoreCase))
        {
            builder.HighestValue(highColor);
            return Result.Ok();
        }

        var stopResult = ParseColorScaleStop("high", rule.HighType!, rule.HighValue);
        if (stopResult.IsFailure)
        {
            return stopResult;
        }
        var stop = stopResult.Value;

        if (stop.IsFormula)
        {
            builder.Maximum(stop.ContentType, stop.FormulaValue, highColor);
        }
        else
        {
            builder.Maximum(stop.ContentType, stop.NumericValue, highColor);
        }
        return Result.Ok();
    }

    private static Result ApplyHighStopThreeStop(IXLCFColorScaleMax builder, ConditionalFormatRule rule, XLColor highColor)
    {
        if (string.IsNullOrEmpty(rule.HighType) || string.Equals(rule.HighType, "max", StringComparison.OrdinalIgnoreCase))
        {
            builder.HighestValue(highColor);
            return Result.Ok();
        }

        var stopResult = ParseColorScaleStop("high", rule.HighType!, rule.HighValue);
        if (stopResult.IsFailure)
        {
            return stopResult;
        }
        var stop = stopResult.Value;

        if (stop.IsFormula)
        {
            builder.Maximum(stop.ContentType, stop.FormulaValue, highColor);
        }
        else
        {
            builder.Maximum(stop.ContentType, stop.NumericValue, highColor);
        }
        return Result.Ok();
    }

    private record struct ColorScaleStop(XLCFContentType ContentType, double NumericValue, string FormulaValue, bool IsFormula);

    private static Result<ColorScaleStop> ParseColorScaleStop(string position, string type, string? value)
    {
        var lowerType = type.ToLowerInvariant();

        XLCFContentType contentType;
        switch (lowerType)
        {
            case "number":
                contentType = XLCFContentType.Number;
                break;
            case "percent":
                contentType = XLCFContentType.Percent;
                break;
            case "percentile":
                contentType = XLCFContentType.Percentile;
                break;
            case "formula":
                contentType = XLCFContentType.Formula;
                break;
            default:
                return Result.Fail($"Unknown {position} stop type '{type}'. Expected 'number', 'percent', 'percentile' or 'formula'.");
        }

        if (string.IsNullOrEmpty(value))
        {
            return Result.Fail($"{position} stop with type '{type}' requires a value.");
        }

        if (lowerType == "formula")
        {
            var formula = value.StartsWith('=') ? value.Substring(1) : value;
            return new ColorScaleStop(contentType, 0d, formula, true);
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return Result.Fail($"{position} stop value '{value}' is not a valid number.");
        }

        return new ColorScaleStop(contentType, numeric, string.Empty, false);
    }
}
