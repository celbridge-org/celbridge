using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_write_cells: the number of cell edits applied.
/// </summary>
public record class WriteCellsResult(int CellCount);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Writes a batch of single-cell edits to a worksheet. Each edit is an object with a "cell"
    /// (A1 address), a "value" (number, boolean, string, or null to blank the cell), and an optional
    /// "isFormula" flag. Strings beginning with '=' are written as text by default. Set isFormula true
    /// to write a formula. Formulas are recalculated as part of the save, so a follow-up
    /// spreadsheet_read_sheet returns fresh computed values. Other cells in the sheet, including
    /// formatting on cells the edits do not touch, are preserved.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to write into. The sheet must already exist.</param>
    /// <param name="edits">JSON array of edit objects, each with fields: cell (A1 string, required), value (number, boolean, string, or null), isFormula (bool, optional, default false).</param>
    /// <returns>JSON object with field: cellCount (int, the number of edits applied).</returns>
    [McpServerTool(Name = "spreadsheet_write_cells")]
    [ToolAlias("spreadsheet.write_cells")]
    public async partial Task<CallToolResult> WriteCells(string resource, string sheet, string edits)
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

        var parseResult = ParseCellEdits(edits);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var cellEdits = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetWriteCellsCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Edits = cellEdits;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var result = new WriteCellsResult(cellEdits.Count);
        return ToolSuccess(SerializeJson(result));
    }

    private static Result<List<SpreadsheetCellEdit>> ParseCellEdits(string editsJson)
    {
        if (string.IsNullOrEmpty(editsJson))
        {
            return Result<List<SpreadsheetCellEdit>>.Fail("Edits JSON is required.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(editsJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result<List<SpreadsheetCellEdit>>.Fail($"Invalid edits JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return Result<List<SpreadsheetCellEdit>>.Fail("Edits JSON must be an array.");
        }

        var cellEdits = new List<SpreadsheetCellEdit>();
        var index = 0;
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return Result<List<SpreadsheetCellEdit>>.Fail($"Edit at index {index} must be an object.");
            }

            if (!entry.TryGetProperty("cell", out var cellElement) ||
                cellElement.ValueKind != JsonValueKind.String)
            {
                return Result<List<SpreadsheetCellEdit>>.Fail($"Edit at index {index} requires a 'cell' string field.");
            }
            var cell = cellElement.GetString() ?? string.Empty;

            object? value = null;
            if (entry.TryGetProperty("value", out var valueElement))
            {
                value = JsonElementToObject(valueElement);
            }

            var isFormula = false;
            if (entry.TryGetProperty("isFormula", out var isFormulaElement))
            {
                if (isFormulaElement.ValueKind == JsonValueKind.True)
                {
                    isFormula = true;
                }
                else if (isFormulaElement.ValueKind != JsonValueKind.False)
                {
                    return Result<List<SpreadsheetCellEdit>>.Fail($"Edit at index {index} has a non-boolean 'isFormula' field.");
                }
            }

            cellEdits.Add(new SpreadsheetCellEdit(cell, value, isFormula));
            index++;
        }

        return Result<List<SpreadsheetCellEdit>>.Ok(cellEdits);
    }
}
