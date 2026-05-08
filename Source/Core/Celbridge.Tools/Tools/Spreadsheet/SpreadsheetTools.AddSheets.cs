using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds empty worksheets to a workbook, appended in the order given.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheetsJson">JSON array of sheet name strings. Names must be unique within the batch and not collide with existing sheets.</param>
    /// <returns>JSON object with the names added in append order.</returns>
    [McpServerTool(Name = "spreadsheet_add_sheets")]
    [ToolAlias("spreadsheet.add_sheets")]
    public async partial Task<CallToolResult> AddSheets(string resource, string sheetsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        var parseResult = ParseSheetNames(sheetsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var sheetNames = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IAddSheetsCommand, AddSheetsResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheets = sheetNames;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }

    private static Result<IReadOnlyList<string>> ParseSheetNames(string sheetsJson)
    {
        if (string.IsNullOrEmpty(sheetsJson))
        {
            return Result.Fail("Sheets JSON is required.");
        }

        try
        {
            var sheets = JsonSerializer.Deserialize<List<string>>(sheetsJson);
            if (sheets is null)
            {
                return Result.Fail("Sheets JSON must be a non-null array.");
            }
            if (sheets.Count == 0)
            {
                return Result.Fail("Sheets array must contain at least one name.");
            }
            return sheets;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid sheets JSON: {ex.Message}");
        }
    }
}
