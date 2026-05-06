using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_freeze_panes: the sheet name and the number
/// of rows and columns frozen.
/// </summary>
public record class FreezePanesResult(string Sheet, int Rows, int Columns);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Freezes the first N rows and/or the first M columns of a worksheet so they remain visible while
    /// the rest of the sheet scrolls. The two axes are independent. Each frozen band is always anchored
    /// at the top-left of the sheet. Either argument may be 0 to leave that axis unfrozen. Setting both
    /// to 0 clears any existing freeze on the sheet.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet whose panes should be frozen.</param>
    /// <param name="rows">Number of rows from the top to freeze. 0 leaves rows unfrozen.</param>
    /// <param name="columns">Number of columns from the left to freeze. 0 leaves columns unfrozen.</param>
    /// <returns>JSON object with fields: sheet (string), rows (int), columns (int).</returns>
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
        var commandResult = await ExecuteCommandAsync<IFreezePanesCommand, SpreadsheetFreezePanesResult>(command =>
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
        var result = new FreezePanesResult(commandValue.Sheet, commandValue.Rows, commandValue.Columns);

        return ToolSuccess(SerializeJson(result));
    }
}
