using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Add empty worksheets to a workbook, appended in the given order.</summary>
    [McpServerTool(Name = "spreadsheet_add_sheets")]
    [ToolAlias("spreadsheet.add_sheets")]
    [RelatedGuides("resource_keys", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> AddSheets(string resource, string sheetsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }

        var parseResult = ParseSheetNames(sheetsJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
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
            return ToolResponse.Error(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
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
