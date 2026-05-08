using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Appends rows to the end of a worksheet's used range. Values starting with '=' are written as text.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to append to. Must already exist.</param>
    /// <param name="rowsJson">JSON array of rows. Each row is an array of cell values starting at column A.</param>
    /// <returns>JSON object with appendedRowCount, firstRow, and lastRow (1-based).</returns>
    [McpServerTool(Name = "spreadsheet_append_rows")]
    [ToolAlias("spreadsheet.append_rows")]
    public async partial Task<CallToolResult> AppendRows(string resource, string sheet, string rowsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        var parseResult = ParseRows(rowsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var parsedRows = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IAppendRowsCommand, AppendRowsResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Rows = parsedRows;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }

    private static Result<List<IReadOnlyList<object?>>> ParseRows(string rowsJson)
    {
        if (string.IsNullOrEmpty(rowsJson))
        {
            return Result.Fail("Rows JSON is required.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(rowsJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid rows JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail("Rows JSON must be an array of row arrays.");
        }

        var parsedRows = new List<IReadOnlyList<object?>>();
        var rowIndex = 0;
        foreach (var rowElement in root.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Array)
            {
                return Result.Fail($"Row at index {rowIndex} must be an array of cell values.");
            }

            var rowValues = new List<object?>();
            foreach (var cellElement in rowElement.EnumerateArray())
            {
                rowValues.Add(JsonElementToObject(cellElement));
            }
            parsedRows.Add(rowValues);
            rowIndex++;
        }

        return parsedRows;
    }
}
