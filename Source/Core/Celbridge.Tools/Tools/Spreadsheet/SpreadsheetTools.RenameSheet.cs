using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Rename a worksheet to a new unique name.</summary>
    [McpServerTool(Name = "spreadsheet_rename_sheet")]
    [ToolAlias("spreadsheet.rename_sheet")]
    public async partial Task<CallToolResult> RenameSheet(string resource, string sheet, string newName)
    {
        const string ToolGuide = "spreadsheet_rename_sheet";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.", ToolGuide);
        }

        if (string.IsNullOrEmpty(newName))
        {
            return ToolResponse.Error("New sheet name is required.", ToolGuide);
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IRenameSheetCommand, RenameSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.NewName = newName;
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
