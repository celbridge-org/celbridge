using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Moves a worksheet to a new 1-based tab position.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to move.</param>
    /// <param name="position">1-based tab position. Must be in [1, sheetCount].</param>
    /// <returns>JSON object with sheet and the new position.</returns>
    [McpServerTool(Name = "spreadsheet_move_sheet")]
    [ToolAlias("spreadsheet.move_sheet")]
    public async partial Task<CallToolResult> MoveSheet(string resource, string sheet, int position)
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

        if (position < 1)
        {
            return ToolError($"Position must be 1 or greater, was {position}.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IMoveSheetCommand, MoveSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Position = position;
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
