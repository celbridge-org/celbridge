using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_clear: how many operations were applied
/// and the total number of cells whose state was reset. A cell is counted
/// when the clear actually changed something on it (value, formula,
/// formatting, comment, merged-range membership, or data validation).
/// Already-default cells inside the targeted range are not counted.
/// </summary>
public record class ClearResult(int OperationsApplied, int CellCount);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Clears cell content, formatting, comments, merged ranges, and data validation across a
    /// batch of ranges in one or more sheets in a single open/save cycle. Each operation specifies
    /// a sheet and a range. range may be a cell range ("A1:C3"), single cell ("B2"), column or
    /// column range ("E", "B:D"), row or row range ("3", "3:5"), or empty string to clear the
    /// entire sheet. Unlike spreadsheet_delete, clear does NOT shift remaining cells — the cleared
    /// range is emptied in place. When the entire sheet is cleared, the sheet's identity (tab
    /// name, position, color, frozen panes, named ranges, column widths, row heights) is
    /// preserved. If any operation fails, the whole batch fails and nothing is saved.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="operationsJson">JSON array of operations. Each operation is an object with sheet (string) and range (string) fields. range is any A1 form: cell range "A1:C3", single cell "B2", column letter or range "E"/"B:D", row number or range "3"/"3:5", or empty string to clear the entire sheet. Do not include a sheet qualifier in range.</param>
    /// <returns>JSON object with fields: operationsApplied (int), cellCount (int, total cells whose state was reset across operations — includes formatting-only cells; already-default cells do not count).</returns>
    [McpServerTool(Name = "spreadsheet_clear")]
    [ToolAlias("spreadsheet.clear")]
    public async partial Task<CallToolResult> Clear(string resource, string operationsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        var parseResult = ParseClearOperations(operationsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var operations = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IClearRangesCommand, SpreadsheetClearRangesResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Operations = operations;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var result = new ClearResult(commandValue.OperationsApplied, commandValue.CellCount);

        return ToolSuccess(SerializeJson(result));
    }

    private static Result<IReadOnlyList<SpreadsheetClearRangesOperation>> ParseClearOperations(string operationsJson)
    {
        if (string.IsNullOrEmpty(operationsJson))
        {
            return Result.Fail("Operations JSON is required.");
        }

        try
        {
            var operations = JsonSerializer.Deserialize<List<SpreadsheetClearRangesOperation>>(operationsJson, JsonOptions);
            if (operations is null)
            {
                return Result.Fail("Operations JSON must be a non-null array.");
            }
            if (operations.Count == 0)
            {
                return Result.Fail("Operations array must contain at least one operation.");
            }
            return operations;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid operations JSON: {ex.Message}");
        }
    }
}
