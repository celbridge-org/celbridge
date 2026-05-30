using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Add conditional formatting rules to a cell range, optionally replacing prior rules.</summary>
    [McpServerTool(Name = "spreadsheet_set_conditional_formatting")]
    [ToolAlias("spreadsheet.set_conditional_formatting")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> SetConditionalFormatting(
        string resource,
        string sheet,
        string range,
        string rulesJson,
        bool clearExisting = false)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        if (string.IsNullOrEmpty(range))
        {
            return ToolResponse.Error("Range is required.");
        }

        var parseResult = ParseJsonArgument<List<ConditionalFormatRule>>(rulesJson, "rules JSON");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var rules = parseResult.Value;

        if (rules.Count == 0 && !clearExisting)
        {
            return ToolResponse.Error("Rules array must contain at least one rule when clearExisting is false.");
        }

        var commandResult = await ExecuteCommandAsync<ISetConditionalFormattingCommand, SetConditionalFormattingResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Sheet = sheet;
            command.Range = range;
            command.Rules = rules;
            command.ClearExisting = clearExisting;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }

}
