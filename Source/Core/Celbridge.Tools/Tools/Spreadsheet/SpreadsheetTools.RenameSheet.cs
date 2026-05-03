using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_rename_sheet: the previous and new sheet names.
/// </summary>
public record class RenameSheetResult(string PreviousName, string NewName);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Renames a worksheet in a workbook. Returns an error if the source sheet does not exist or if
    /// the new name collides with another sheet.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Current name of the worksheet to rename.</param>
    /// <param name="newName">New name to assign to the worksheet.</param>
    /// <returns>JSON object with fields: previousName (string), newName (string).</returns>
    [McpServerTool(Name = "spreadsheet_rename_sheet")]
    [ToolAlias("spreadsheet.rename_sheet")]
    public async partial Task<CallToolResult> RenameSheet(string resource, string sheet, string newName)
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

        if (string.IsNullOrEmpty(newName))
        {
            return ErrorResult("New sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetRenameSheetCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.NewName = newName;
        });
        if (commandResult.IsError == true)
        {
            return commandResult;
        }

        return SuccessResult(SerializeJson(new RenameSheetResult(sheet, newName)));
    }
}
