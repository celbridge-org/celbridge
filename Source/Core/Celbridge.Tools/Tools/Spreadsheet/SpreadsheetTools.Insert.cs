using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_insert: how many operations were applied
/// and how many rows and columns were inserted in total across them.
/// </summary>
public record class InsertResult(int OperationsApplied, int InsertedRowCount, int InsertedColumnCount);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Inserts empty rows or columns into one or more sheets in a single open/save cycle. Each
    /// operation specifies a sheet and a row range ("3" or "3:5") or column range ("B" or "B:D").
    /// Cell ranges (e.g. "A1:C3") are not accepted — Excel's "shift cells down/right" is
    /// intentionally not exposed. The width of the range determines how many empty rows or
    /// columns are inserted (e.g. "3:5" inserts 3 rows starting at row 3, "B:D" inserts 3
    /// columns starting at column B). Indices are interpreted against the original workbook
    /// state, so an agent can specify "insert rows at 3 and at 10" without having to mentally
    /// shift indices after earlier inserts; the implementation applies inserts in descending
    /// order to make the original-coordinate semantics work, and overlapping ranges are
    /// deduped. Existing rows at or below the insert position shift down; existing columns at
    /// or to the right of the insert position shift right. Formulas are recalculated as part
    /// of the save. If any operation fails, the whole batch fails and nothing is saved.
    /// Mirrors spreadsheet_delete: insert and delete are inverse structural operations.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="operationsJson">JSON array of operations. Each operation is an object with sheet (string) and range (string) fields. range is "3" or "3:5" for rows, "B" or "B:D" for columns. Do not include a sheet qualifier in range.</param>
    /// <returns>JSON object with fields: operationsApplied (int), insertedRowCount (int), insertedColumnCount (int).</returns>
    [McpServerTool(Name = "spreadsheet_insert")]
    [ToolAlias("spreadsheet.insert")]
    public async partial Task<CallToolResult> Insert(string resource, string operationsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        var parseResult = ParseInsertOperations(operationsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var operations = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetInsertCommand, SpreadsheetInsertResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Operations = operations;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var result = new InsertResult(commandValue.OperationsApplied, commandValue.InsertedRowCount, commandValue.InsertedColumnCount);

        return ToolSuccess(SerializeJson(result));
    }

    private static Result<IReadOnlyList<SpreadsheetInsertOperation>> ParseInsertOperations(string operationsJson)
    {
        if (string.IsNullOrEmpty(operationsJson))
        {
            return Result.Fail("Operations JSON is required.");
        }

        try
        {
            var operations = JsonSerializer.Deserialize<List<SpreadsheetInsertOperation>>(operationsJson, JsonOptions);
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
