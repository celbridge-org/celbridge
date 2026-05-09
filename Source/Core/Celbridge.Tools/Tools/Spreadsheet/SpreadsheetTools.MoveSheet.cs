using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Move a worksheet to a new 1-based tab position within the workbook.</summary>
    [McpServerTool(Name = "spreadsheet_move_sheet")]
    [ToolAlias("spreadsheet.move_sheet")]
    public async partial Task<CallToolResult> MoveSheet(string resource, string sheet, int position)
    {
        const string ToolGuide = "spreadsheet_move_sheet";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.", ToolGuide);
        }

        if (position < 1)
        {
            return ToolResponse.Error($"Position must be 1 or greater, was {position}.", ToolGuide);
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
            return ToolResponse.Error(commandResult, ToolGuide);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }
}
