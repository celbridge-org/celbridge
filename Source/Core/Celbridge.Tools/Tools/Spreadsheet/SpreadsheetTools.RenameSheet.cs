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
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        if (string.IsNullOrEmpty(newName))
        {
            return ToolError("New sheet name is required.");
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
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }
}
