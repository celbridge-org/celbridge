using Celbridge.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class SetConditionalFormattingCommand : CommandBase, ISpreadsheetSetConditionalFormattingCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public IReadOnlyList<SpreadsheetConditionalFormatRule> Rules { get; set; } = Array.Empty<SpreadsheetConditionalFormatRule>();
    public bool ClearExisting { get; set; }

    public SpreadsheetSetConditionalFormattingResult ResultValue { get; private set; } =
        new SpreadsheetSetConditionalFormattingResult(0, 0);

    public SetConditionalFormattingCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resolveResult = SpreadsheetCommandHelpers.ResolveWorkbookPath(_workspaceWrapper, FileResource);
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

        if (SpreadsheetCommandHelpers.IsColumnRange(Range) || SpreadsheetCommandHelpers.IsRowRange(Range))
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
                var addressesToRemove = new HashSet<string>();
                foreach (var existing in worksheet.ConditionalFormats)
                {
                    if (existing.Range.RangeAddress.Intersects(targetRange.RangeAddress))
                    {
                        var existingAddress = existing.Range.RangeAddress.ToString() ?? string.Empty;
                        addressesToRemove.Add(existingAddress);
                    }
                }
                rulesRemoved = addressesToRemove.Count;
                worksheet.ConditionalFormats.Remove(format =>
                {
                    var formatAddress = format.Range.RangeAddress.ToString() ?? string.Empty;
                    return addressesToRemove.Contains(formatAddress);
                });
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

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetSetConditionalFormattingResult(Rules.Count, rulesRemoved);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set conditional formatting on '{Sheet}!{Range}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    private static Result ApplyRule(IXLRange targetRange, SpreadsheetConditionalFormatRule rule)
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

    private static Result<IXLStyle> ApplyComparisonRule(IXLConditionalFormat conditionalFormat, string typeKey, SpreadsheetConditionalFormatRule rule)
    {
        IXLStyle style;
        switch (typeKey)
        {
            case "greaterthan":
                if (!rule.Value.HasValue) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenGreaterThan(rule.Value.Value);
                break;
            case "greaterthanorequal":
                if (!rule.Value.HasValue) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenEqualOrGreaterThan(rule.Value.Value);
                break;
            case "lessthan":
                if (!rule.Value.HasValue) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenLessThan(rule.Value.Value);
                break;
            case "lessthanorequal":
                if (!rule.Value.HasValue) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenEqualOrLessThan(rule.Value.Value);
                break;
            case "equal":
                if (!rule.Value.HasValue) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenEquals(rule.Value.Value);
                break;
            case "notequal":
                if (!rule.Value.HasValue) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires a numeric value.");
                style = conditionalFormat.WhenNotEquals(rule.Value.Value);
                break;
            case "between":
                if (!rule.Value.HasValue || !rule.Value2.HasValue)
                    return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires both 'value' and 'value2'.");
                style = conditionalFormat.WhenBetween(rule.Value.Value, rule.Value2.Value);
                break;
            case "notbetween":
                if (!rule.Value.HasValue || !rule.Value2.HasValue)
                    return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires both 'value' and 'value2'.");
                style = conditionalFormat.WhenNotBetween(rule.Value.Value, rule.Value2.Value);
                break;
            case "containstext":
                if (string.IsNullOrEmpty(rule.Text)) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenContains(rule.Text);
                break;
            case "doesnotcontaintext":
                if (string.IsNullOrEmpty(rule.Text)) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenNotContains(rule.Text);
                break;
            case "beginswith":
                if (string.IsNullOrEmpty(rule.Text)) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires 'text'.");
                style = conditionalFormat.WhenStartsWith(rule.Text);
                break;
            case "endswith":
                if (string.IsNullOrEmpty(rule.Text)) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires 'text'.");
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
                if (string.IsNullOrEmpty(rule.Formula)) return Result<IXLStyle>.Fail($"'{rule.Type}' rule requires 'formula'.");
                var formula = rule.Formula.StartsWith('=') ? rule.Formula.Substring(1) : rule.Formula;
                style = conditionalFormat.WhenIsTrue(formula);
                break;
            default:
                return Result<IXLStyle>.Fail($"Unknown rule type: '{rule.Type}'.");
        }

        return style.OkResult();
    }

    private static Result ApplyRuleFormatting(IXLStyle style, SpreadsheetConditionalFormatRule rule)
    {
        if (rule.BackgroundColor is not null)
        {
            var colorResult = SpreadsheetFormatConverter.ParseColor(rule.BackgroundColor);
            if (colorResult.IsFailure)
            {
                return colorResult;
            }
            style.Fill.PatternType = XLFillPatternValues.Solid;
            style.Fill.BackgroundColor = colorResult.Value;
        }

        if (rule.FontColor is not null)
        {
            var colorResult = SpreadsheetFormatConverter.ParseColor(rule.FontColor);
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

    private static Result ApplyColorScale2(IXLConditionalFormat conditionalFormat, SpreadsheetConditionalFormatRule rule)
    {
        var lowColorHex = rule.LowColor ?? "#FFFFFF";
        var highColorHex = rule.HighColor ?? "#FF0000";

        var lowColorResult = SpreadsheetFormatConverter.ParseColor(lowColorHex);
        if (lowColorResult.IsFailure)
        {
            return lowColorResult;
        }
        var highColorResult = SpreadsheetFormatConverter.ParseColor(highColorHex);
        if (highColorResult.IsFailure)
        {
            return highColorResult;
        }

        conditionalFormat.ColorScale()
            .LowestValue(lowColorResult.Value)
            .HighestValue(highColorResult.Value);

        return Result.Ok();
    }

    private static Result ApplyColorScale3(IXLConditionalFormat conditionalFormat, SpreadsheetConditionalFormatRule rule)
    {
        var lowColorHex = rule.LowColor ?? "#FF0000";
        var midColorHex = rule.MidColor ?? "#FFFFFF";
        var highColorHex = rule.HighColor ?? "#00FF00";

        var lowColorResult = SpreadsheetFormatConverter.ParseColor(lowColorHex);
        if (lowColorResult.IsFailure)
        {
            return lowColorResult;
        }
        var midColorResult = SpreadsheetFormatConverter.ParseColor(midColorHex);
        if (midColorResult.IsFailure)
        {
            return midColorResult;
        }
        var highColorResult = SpreadsheetFormatConverter.ParseColor(highColorHex);
        if (highColorResult.IsFailure)
        {
            return highColorResult;
        }

        conditionalFormat.ColorScale()
            .LowestValue(lowColorResult.Value)
            .Midpoint(XLCFContentType.Percent, 50d, midColorResult.Value)
            .HighestValue(highColorResult.Value);

        return Result.Ok();
    }
}
