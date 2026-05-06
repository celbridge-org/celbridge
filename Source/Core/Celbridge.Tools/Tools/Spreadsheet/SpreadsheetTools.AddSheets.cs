using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_add_sheets: the names of the sheets that
/// were added, in append order.
/// </summary>
public record class AddSheetsResult(IReadOnlyList<string> Sheets);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds one or more empty worksheets to a workbook in a single open/save cycle. Sheets are appended
    /// after the existing sheets, in the order given. Returns an error if any requested name collides
    /// with an existing sheet or with another name in the same batch. In that case nothing is saved.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheetsJson">JSON array of sheet name strings (e.g. ["Q1", "Q2", "Q3"]). Names must be unique within the batch and must not collide with existing sheets in the workbook.</param>
    /// <returns>JSON object with field: sheets (string[], the names added in append order).</returns>
    [McpServerTool(Name = "spreadsheet_add_sheets")]
    [ToolAlias("spreadsheet.add_sheets")]
    public async partial Task<CallToolResult> AddSheets(string resource, string sheetsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult);
        }

        var parseResult = ParseSheetNames(sheetsJson);
        if (parseResult.IsFailure)
        {
            return ErrorResult(parseResult);
        }
        var sheetNames = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var (callResult, commandResult) = await ExecuteCommandAsync<ISpreadsheetAddSheetsCommand, SpreadsheetAddSheetsResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheets = sheetNames;
        });
        if (callResult.IsError == true)
        {
            return callResult;
        }

        var commandValue = commandResult ?? new SpreadsheetAddSheetsResult(sheetNames);
        var result = new AddSheetsResult(commandValue.Sheets);

        return SuccessResult(SerializeJson(result));
    }

    private static Result<IReadOnlyList<string>> ParseSheetNames(string sheetsJson)
    {
        if (string.IsNullOrEmpty(sheetsJson))
        {
            return Result<IReadOnlyList<string>>.Fail("Sheets JSON is required.");
        }

        try
        {
            var sheets = JsonSerializer.Deserialize<List<string>>(sheetsJson);
            if (sheets is null)
            {
                return Result<IReadOnlyList<string>>.Fail("Sheets JSON must be a non-null array.");
            }
            if (sheets.Count == 0)
            {
                return Result<IReadOnlyList<string>>.Fail("Sheets array must contain at least one name.");
            }
            return Result<IReadOnlyList<string>>.Ok(sheets);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<string>>.Fail($"Invalid sheets JSON: {ex.Message}");
        }
    }
}
