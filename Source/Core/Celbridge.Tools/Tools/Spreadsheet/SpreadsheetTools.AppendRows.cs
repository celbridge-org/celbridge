using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_append_rows: how many rows were appended and
/// the 1-based row range they now occupy.
/// </summary>
public record class AppendRowsResult(int AppendedRowCount, int FirstRow, int LastRow);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Appends rows to the end of a worksheet's used range. Each row is an array of cell values
    /// (number, boolean, string, or null) starting at column A. An empty sheet receives the rows
    /// starting at A1. Cell values that begin with '=' are written as text. Use spreadsheet_write_cells
    /// for formula writes. Formulas elsewhere in the workbook are recalculated as part of the save.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to append to. The sheet must already exist.</param>
    /// <param name="rows">JSON array of rows. Each row is an array of cell values (number, boolean, string, or null) in column order starting from A.</param>
    /// <returns>JSON object with fields: appendedRowCount (int), firstRow (int, 1-based), lastRow (int, 1-based).</returns>
    [McpServerTool(Name = "spreadsheet_append_rows")]
    [ToolAlias("spreadsheet.append_rows")]
    public async partial Task<CallToolResult> AppendRows(string resource, string sheet, string rows)
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

        var parseResult = ParseRows(rows);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var parsedRows = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetAppendRowsCommand, SpreadsheetAppendRowsResult>(command =>
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
        var result = new AppendRowsResult(commandValue.AppendedRowCount, commandValue.FirstRow, commandValue.LastRow);
        return ToolSuccess(SerializeJson(result));
    }

    private static Result<List<IReadOnlyList<object?>>> ParseRows(string rowsJson)
    {
        if (string.IsNullOrEmpty(rowsJson))
        {
            return Result<List<IReadOnlyList<object?>>>.Fail("Rows JSON is required.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(rowsJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result<List<IReadOnlyList<object?>>>.Fail($"Invalid rows JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return Result<List<IReadOnlyList<object?>>>.Fail("Rows JSON must be an array of row arrays.");
        }

        var parsedRows = new List<IReadOnlyList<object?>>();
        var rowIndex = 0;
        foreach (var rowElement in root.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Array)
            {
                return Result<List<IReadOnlyList<object?>>>.Fail($"Row at index {rowIndex} must be an array of cell values.");
            }

            var rowValues = new List<object?>();
            foreach (var cellElement in rowElement.EnumerateArray())
            {
                rowValues.Add(JsonElementToObject(cellElement));
            }
            parsedRows.Add(rowValues);
            rowIndex++;
        }

        return Result<List<IReadOnlyList<object?>>>.Ok(parsedRows);
    }
}
