using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_add_sheet: the name of the sheet that was added.
/// </summary>
public record class AddSheetResult(string Sheet);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds a new empty worksheet to a workbook. The sheet is appended after the existing sheets.
    /// Returns an error if a worksheet with the same name already exists.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to add.</param>
    /// <returns>JSON object with field: sheet (string, the added sheet name).</returns>
    [McpServerTool(Name = "spreadsheet_add_sheet")]
    [ToolAlias("spreadsheet.add_sheet")]
    public async partial Task<CallToolResult> AddSheet(string resource, string sheet)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetAddSheetCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
        });
        if (commandResult.IsError == true)
        {
            return commandResult;
        }

        return SuccessResult(SerializeJson(new AddSheetResult(sheet)));
    }
}
