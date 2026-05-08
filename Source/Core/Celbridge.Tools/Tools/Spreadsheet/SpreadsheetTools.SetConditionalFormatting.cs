using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds one or more conditional formatting rules to a cell range.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to apply rules to.</param>
    /// <param name="range">A1 cell range. Column-letter and row-number ranges are rejected.</param>
    /// <param name="rulesJson">JSON array of rule objects. See guides_read(['spreadsheet_set_conditional_formatting']) for the full rule type catalog.</param>
    /// <param name="clearExisting">When true, pre-existing rules whose ranges intersect the target range are removed first.</param>
    /// <returns>JSON object with rulesApplied and rulesRemoved counts.</returns>
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
        var commandResult = await ExecuteCommandAsync<ISetConditionalFormattingCommand, SetConditionalFormattingResult>(command =>
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
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }

    private static Result<IReadOnlyList<ConditionalFormatRule>> ParseConditionalFormatRules(string rulesJson)
    {
        if (string.IsNullOrEmpty(rulesJson))
        {
            return Result.Fail("Rules JSON is required.");
        }

        try
        {
            var rules = JsonSerializer.Deserialize<List<ConditionalFormatRule>>(rulesJson, JsonOptions);
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
