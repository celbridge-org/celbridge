using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Deletes contiguous ranges of rows or columns from one or more sheets in a single open/save
    /// cycle. Each operation specifies a sheet and a row range ("3" or "3:5") or column range
    /// ("B" or "B:D"). Cell ranges (e.g. "A1:C3") are not accepted — Excel's "shift cells up/left"
    /// is intentionally not exposed. Indices are interpreted against the original workbook state,
    /// so an agent can specify "rows 3:5 and 10" without having to mentally shift indices after
    /// earlier deletes; the implementation applies deletes in descending order to make the
    /// original-coordinate semantics work, and overlapping ranges are deduped. Rows below a
    /// deleted row range shift up; columns to the right of a deleted column range shift left.
    /// Formulas are recalculated as part of the save. If any operation fails, the whole batch
    /// fails and nothing is saved.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="operationsJson">JSON array of operations. Each operation is an object with sheet (string) and range (string) fields. range is "3" or "3:5" for rows, "B" or "B:D" for columns. Do not include a sheet qualifier in range.</param>
    /// <returns>JSON object with fields: operationsApplied (int), deletedRowCount (int), deletedColumnCount (int).</returns>
    [McpServerTool(Name = "spreadsheet_delete")]
    [ToolAlias("spreadsheet.delete")]
    public async partial Task<CallToolResult> Delete(string resource, string operationsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        var parseResult = ParseDeleteOperations(operationsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var operations = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IDeleteRangesCommand, DeleteRangesResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Operations = operations;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }

    private static Result<IReadOnlyList<DeleteRangesOperation>> ParseDeleteOperations(string operationsJson)
    {
        if (string.IsNullOrEmpty(operationsJson))
        {
            return Result.Fail("Operations JSON is required.");
        }

        try
        {
            var operations = JsonSerializer.Deserialize<List<DeleteRangesOperation>>(operationsJson, JsonOptions);
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
