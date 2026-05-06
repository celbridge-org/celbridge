using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_remove_sheet: the name of the sheet that was removed.
/// </summary>
public record class RemoveSheetResult(string Sheet);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Removes a worksheet from a workbook. Returns an error if the sheet does not exist, or if it is
    /// the only remaining sheet (a workbook must contain at least one sheet).
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to remove.</param>
    /// <returns>JSON object with field: sheet (string, the removed sheet name).</returns>
    [McpServerTool(Name = "spreadsheet_remove_sheet")]
    [ToolAlias("spreadsheet.remove_sheet")]
    public async partial Task<CallToolResult> RemoveSheet(string resource, string sheet)
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

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetRemoveSheetCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
        });
        if (commandResult.IsError == true)
        {
            return commandResult;
        }

        return SuccessResult(SerializeJson(new RemoveSheetResult(sheet)));
    }
}
