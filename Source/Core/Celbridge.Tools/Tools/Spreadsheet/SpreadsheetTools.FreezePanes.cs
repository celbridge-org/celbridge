using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Freeze the top N rows and/or left M columns of a worksheet for scroll locking.</summary>
    [McpServerTool(Name = "spreadsheet_freeze_panes")]
    [ToolAlias("spreadsheet.freeze_panes")]
    public async partial Task<CallToolResult> FreezePanes(string resource, string sheet, int rows = 0, int columns = 0)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        if (rows < 0 || columns < 0)
        {
            return ToolResponse.Error("rows and columns must be non-negative.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IFreezePanesCommand, FreezePanesResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Rows = rows;
            command.Columns = columns;
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
