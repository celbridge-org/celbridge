using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Duplicate a worksheet with all values, formulas, and formatting under a new name.</summary>
    [McpServerTool(Name = "spreadsheet_duplicate_sheet")]
    [ToolAlias("spreadsheet.duplicate_sheet")]
    [RelatedGuides("resource_keys", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> DuplicateSheet(
        string resource,
        string sourceSheet,
        string newSheet,
        int position = 0)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }

        if (string.IsNullOrEmpty(sourceSheet))
        {
            return ToolResponse.Error("Source sheet name is required.");
        }

        if (string.IsNullOrEmpty(newSheet))
        {
            return ToolResponse.Error("New sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IDuplicateSheetCommand, DuplicateSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.SourceSheet = sourceSheet;
            command.NewSheet = newSheet;
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
