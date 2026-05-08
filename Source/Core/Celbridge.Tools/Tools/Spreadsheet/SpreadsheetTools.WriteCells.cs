using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Write per-cell value or formula edits to a worksheet, leaving other cells untouched.</summary>
    [McpServerTool(Name = "spreadsheet_write_cells")]
    [ToolAlias("spreadsheet.write_cells")]
    public async partial Task<CallToolResult> WriteCells(string resource, string sheet, string editsJson)
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

        var parseResult = ParseCellEdits(editsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var cellEdits = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IWriteCellsCommand, WriteCellsResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Edits = cellEdits;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }

    private static Result<List<CellEdit>> ParseCellEdits(string editsJson)
    {
        if (string.IsNullOrEmpty(editsJson))
        {
            return Result.Fail("Edits JSON is required.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(editsJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid edits JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail("Edits JSON must be an array.");
        }

        var cellEdits = new List<CellEdit>();
        var index = 0;
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return Result.Fail($"Edit at index {index} must be an object.");
            }

            if (!entry.TryGetProperty("cell", out var cellElement) ||
                cellElement.ValueKind != JsonValueKind.String)
            {
                return Result.Fail($"Edit at index {index} requires a 'cell' string field.");
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
                    return Result.Fail($"Edit at index {index} has a non-boolean 'isFormula' field.");
                }
            }

            cellEdits.Add(new CellEdit(cell, value, isFormula));
            index++;
        }

        return cellEdits;
    }
}
