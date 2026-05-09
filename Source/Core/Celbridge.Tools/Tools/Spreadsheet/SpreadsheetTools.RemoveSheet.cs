using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Remove a worksheet from the workbook (at least one sheet must remain).</summary>
    [McpServerTool(Name = "spreadsheet_remove_sheet")]
    [ToolAlias("spreadsheet.remove_sheet")]
    [RelatedGuides("resource_keys", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> RemoveSheet(string resource, string sheet)
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

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IRemoveSheetCommand, RemoveSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
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
