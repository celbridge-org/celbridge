using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_set_conditional_formatting: how many new
/// rules were added and how many pre-existing overlapping rules were removed
/// when clearExisting was true.
/// </summary>
public record class SetConditionalFormattingResult(int RulesApplied, int RulesRemoved);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds one or more conditional formatting rules to a cell range. Conditional formatting drives
    /// cell appearance based on cell values or a formula, so highlights stay correct as data changes
    /// (vs. spreadsheet_format_ranges which sets static styles). Common cases: highlight cells over
    /// or under a threshold, colour scale across a column of numbers, formula-based highlight for
    /// whole rows. Each rule has a type, type-specific inputs (value, value2, text, formula), and
    /// formatting fields (backgroundColor, fontColor, bold, italic) that are applied to matched cells.
    /// Color-scale rules use lowColor / midColor / highColor instead. With clearExisting=true any
    /// pre-existing conditional rules whose ranges intersect the target range are removed first.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to apply rules to.</param>
    /// <param name="range">A1 cell range the rules apply to (e.g. "B2:B100"). Column-letter and row-number ranges are rejected.</param>
    /// <param name="rulesJson">JSON array of rule objects. Each rule has a "type" field plus the inputs that type needs. Types: "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual", "equal", "notEqual", "between", "notBetween" (numeric, use "value" and "value2"); "containsText", "doesNotContainText", "beginsWith", "endsWith" (use "text"); "isBlank", "isNotBlank", "isError", "isNotError", "duplicateValues", "uniqueValues" (no extra inputs); "formula" (use "formula", an Excel formula string with or without leading "="); "colorScale2", "colorScale3" (use "lowColor"/"highColor", and "midColor" for 3-stop). Non-color-scale rules accept "backgroundColor", "fontColor", "bold", "italic" to drive matched-cell formatting. Colors are CSS hex strings (#RRGGBB).</param>
    /// <param name="clearExisting">When true, removes any pre-existing conditional formatting rules whose ranges intersect the target range before adding the new rules. Default false adds the new rules alongside existing ones.</param>
    /// <returns>JSON object with fields: rulesApplied (int, new rules added), rulesRemoved (int, pre-existing rules removed when clearExisting was true; 0 otherwise).</returns>
    [McpServerTool(Name = "spreadsheet_set_conditional_formatting")]
    [ToolAlias("spreadsheet.set_conditional_formatting")]
    public async partial Task<CallToolResult> SetConditionalFormatting(
        string resource,
        string sheet,
        string range,
        string rulesJson,
        bool clearExisting = false)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        if (string.IsNullOrEmpty(range))
        {
            return ToolError("Range is required.");
        }

        var parseResult = ParseConditionalFormatRules(rulesJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var rules = parseResult.Value;

        if (rules.Count == 0 && !clearExisting)
        {
            return ToolError("Rules array must contain at least one rule when clearExisting is false.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetSetConditionalFormattingCommand, SpreadsheetSetConditionalFormattingResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Range = range;
            command.Rules = rules;
            command.ClearExisting = clearExisting;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var result = new SetConditionalFormattingResult(commandValue.RulesApplied, commandValue.RulesRemoved);

        return ToolSuccess(SerializeJson(result));
    }

    private static Result<IReadOnlyList<SpreadsheetConditionalFormatRule>> ParseConditionalFormatRules(string rulesJson)
    {
        if (string.IsNullOrEmpty(rulesJson))
        {
            return Result.Fail("Rules JSON is required.");
        }

        try
        {
            var rules = JsonSerializer.Deserialize<List<SpreadsheetConditionalFormatRule>>(rulesJson, JsonOptions);
            if (rules is null)
            {
                return Result.Fail("Rules JSON must be a non-null array.");
            }
            return rules;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid rules JSON: {ex.Message}");
        }
    }
}
