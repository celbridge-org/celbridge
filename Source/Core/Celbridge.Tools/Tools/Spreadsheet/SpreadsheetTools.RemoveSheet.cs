using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Remove a worksheet from the workbook (at least one sheet must remain).</summary>
    [McpServerTool(Name = "spreadsheet_remove_sheet")]
    [ToolAlias("spreadsheet.remove_sheet")]
    public async partial Task<CallToolResult> RemoveSheet(string resource, string sheet)
    {
        const string ToolGuide = "spreadsheet_remove_sheet";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.", ToolGuide);
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IRemoveSheetCommand, RemoveSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult, ToolGuide);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }
}
