using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_duplicate_sheet: the name of the duplicate
/// sheet and its 1-based tab position in the workbook after the operation.
/// </summary>
public record class DuplicateSheetResult(string NewSheet, int Position);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Duplicates an existing worksheet, copying values, formulas, formatting, conditional formatting,
    /// freeze panes, column widths, row heights, and other sheet-level state. Useful for templating
    /// workflows where you want a new sheet seeded from an existing layout. Fails if the source sheet
    /// does not exist, the new name collides with an existing sheet, or position is out of range.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sourceSheet">Name of the worksheet to duplicate.</param>
    /// <param name="newSheet">Name to give the duplicate. Must not collide with an existing sheet name.</param>
    /// <param name="position">1-based tab position to place the duplicate at, or 0 (default) to append it after existing sheets. Must be in [0, sheetCount + 1].</param>
    /// <returns>JSON object with fields: newSheet (string, the duplicate's name), position (int, the duplicate's 1-based tab position).</returns>
    [McpServerTool(Name = "spreadsheet_duplicate_sheet")]
    [ToolAlias("spreadsheet.duplicate_sheet")]
    public async partial Task<CallToolResult> DuplicateSheet(
        string resource,
        string sourceSheet,
        string newSheet,
        int position = 0)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult);
        }

        if (string.IsNullOrEmpty(sourceSheet))
        {
            return ErrorResult("Source sheet name is required.");
        }

        if (string.IsNullOrEmpty(newSheet))
        {
            return ErrorResult("New sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var (callResult, commandResult) = await ExecuteCommandAsync<ISpreadsheetDuplicateSheetCommand, SpreadsheetDuplicateSheetResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.SourceSheet = sourceSheet;
            command.NewSheet = newSheet;
            command.Position = position;
        });
        if (callResult.IsError == true)
        {
            return callResult;
        }

        var commandValue = commandResult ?? new SpreadsheetDuplicateSheetResult(newSheet, 0);
        var result = new DuplicateSheetResult(commandValue.NewSheet, commandValue.Position);

        return SuccessResult(SerializeJson(result));
    }
}
