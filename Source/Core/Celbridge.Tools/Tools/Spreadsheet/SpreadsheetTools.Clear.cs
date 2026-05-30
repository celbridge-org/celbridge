using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Clear cell content and formatting in ranges without removing the rows or columns.</summary>
    [McpServerTool(Name = "spreadsheet_clear")]
    [ToolAlias("spreadsheet.clear")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> Clear(string resource, string operationsJson)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        var parseResult = ParseJsonArgument<List<ClearRangesOperation>>(operationsJson, "operations JSON");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var operations = parseResult.Value;
        if (operations.Count == 0)
        {
            return ToolResponse.Error("Operations array must contain at least one operation.");
        }

        var commandResult = await ExecuteCommandAsync<IClearRangesCommand, ClearRangesResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Operations = operations;
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
