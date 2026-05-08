using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Removes a worksheet. A workbook must retain at least one sheet.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to remove.</param>
    /// <returns>JSON object with the removed sheet name.</returns>
    [McpServerTool(Name = "spreadsheet_remove_sheet")]
    [ToolAlias("spreadsheet.remove_sheet")]
    public async partial Task<CallToolResult> RemoveSheet(string resource, string sheet)
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

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IRemoveSheetCommand, RemoveSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }
}
