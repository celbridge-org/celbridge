using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Freezes the first N rows and/or M columns of a worksheet. Setting both to 0 clears any freeze.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet whose panes should be frozen.</param>
    /// <param name="rows">Rows from the top to freeze. 0 leaves rows unfrozen.</param>
    /// <param name="columns">Columns from the left to freeze. 0 leaves columns unfrozen.</param>
    /// <returns>JSON object with sheet, rows, and columns.</returns>
    [McpServerTool(Name = "spreadsheet_freeze_panes")]
    [ToolAlias("spreadsheet.freeze_panes")]
    public async partial Task<CallToolResult> FreezePanes(string resource, string sheet, int rows = 0, int columns = 0)
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

        if (rows < 0 || columns < 0)
        {
            return ToolError("rows and columns must be non-negative.");
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
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }
}
