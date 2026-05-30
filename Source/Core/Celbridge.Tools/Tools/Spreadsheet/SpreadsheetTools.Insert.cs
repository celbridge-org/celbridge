using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Insert empty rows or columns, shifting existing cells down or right.</summary>
    [McpServerTool(Name = "spreadsheet_insert")]
    [ToolAlias("spreadsheet.insert")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> Insert(string resource, string operationsJson)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        var parseResult = ParseJsonArgument<List<InsertRangesOperation>>(operationsJson, "operations JSON");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var operations = parseResult.Value;
        if (operations.Count == 0)
        {
            return ToolResponse.Error("Operations array must contain at least one operation.");
        }

        var commandResult = await ExecuteCommandAsync<IInsertRangesCommand, InsertRangesResult>(command =>
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
