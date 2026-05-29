using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Rename a worksheet to a new unique name.</summary>
    [McpServerTool(Name = "spreadsheet_rename_sheet")]
    [ToolAlias("spreadsheet.rename_sheet")]
    [RelatedGuides("resource_keys", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> RenameSheet(string resource, string sheet, string newName)
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

        if (string.IsNullOrEmpty(newName))
        {
            return ToolResponse.Error("New sheet name is required.");
        }

        var commandResult = await ExecuteCommandAsync<IRenameSheetCommand, RenameSheetResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Sheet = sheet;
            command.NewName = newName;
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
