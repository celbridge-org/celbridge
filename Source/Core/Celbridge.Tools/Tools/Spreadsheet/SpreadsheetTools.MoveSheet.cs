using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Move a worksheet to a new 1-based tab position within the workbook.</summary>
    [McpServerTool(Name = "spreadsheet_move_sheet")]
    [ToolAlias("spreadsheet.move_sheet")]
    [RelatedGuides("resource_keys", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> MoveSheet(string resource, string sheet, int position)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        if (position < 1)
        {
            return ToolResponse.Error($"Position must be 1 or greater, was {position}.");
        }

        var commandResult = await ExecuteCommandAsync<IMoveSheetCommand, MoveSheetResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Sheet = sheet;
            command.Position = position;
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
