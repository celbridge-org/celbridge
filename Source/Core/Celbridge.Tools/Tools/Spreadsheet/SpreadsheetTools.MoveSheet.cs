using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_move_sheet: the sheet's name and its new
/// 1-based tab position.
/// </summary>
public record class MoveSheetResult(string Sheet, int Position);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Moves a worksheet to a new 1-based tab position. Position 1 places the sheet first. The maximum
    /// valid position is the current sheet count. Returns an error if the sheet does not exist or the
    /// position is out of range. Use spreadsheet_get_info to read current sheet positions before calling.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to move.</param>
    /// <param name="position">1-based tab position to move the sheet to. Must be in [1, sheetCount].</param>
    /// <returns>JSON object with fields: sheet (string), position (int, the new 1-based position).</returns>
    [McpServerTool(Name = "spreadsheet_move_sheet")]
    [ToolAlias("spreadsheet.move_sheet")]
    public async partial Task<CallToolResult> MoveSheet(string resource, string sheet, int position)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        if (position < 1)
        {
            return ErrorResult($"Position must be 1 or greater, was {position}.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetMoveSheetCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Position = position;
        });
        if (commandResult.IsError == true)
        {
            return commandResult;
        }

        return SuccessResult(SerializeJson(new MoveSheetResult(sheet, position)));
    }
}
