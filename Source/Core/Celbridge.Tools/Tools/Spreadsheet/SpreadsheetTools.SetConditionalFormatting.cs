using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Add conditional formatting rules to a cell range, optionally replacing prior rules.</summary>
    [McpServerTool(Name = "spreadsheet_set_conditional_formatting")]
    [ToolAlias("spreadsheet.set_conditional_formatting")]
    public async partial Task<CallToolResult> SetConditionalFormatting(
        string resource,
        string sheet,
        string range,
        string rulesJson,
        bool clearExisting = false)
    {
        const string ToolGuide = "spreadsheet_set_conditional_formatting";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.", ToolGuide);
        }

        if (string.IsNullOrEmpty(range))
        {
            return ToolResponse.Error("Range is required.", ToolGuide);
        }

        var parseResult = ParseConditionalFormatRules(rulesJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult, ToolGuide);
        }
        var rules = parseResult.Value;

        if (rules.Count == 0 && !clearExisting)
        {
            return ToolResponse.Error("Rules array must contain at least one rule when clearExisting is false.", ToolGuide);
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
            return ToolResponse.Error(commandResult, ToolGuide);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
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
