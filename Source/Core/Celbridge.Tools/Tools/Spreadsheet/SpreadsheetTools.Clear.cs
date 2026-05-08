using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Clears cell content, formatting, comments, merged ranges, and data validation across a batch of ranges.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="operationsJson">JSON array of operations with sheet and range fields. range accepts any A1 form including empty string for the entire sheet. See guides_read(['spreadsheet_clear']) for the clear-vs-delete contrast.</param>
    /// <returns>JSON object with operationsApplied and cellCount.</returns>
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
        var commandResult = await ExecuteCommandAsync<IClearRangesCommand, ClearRangesResult>(command =>
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

    private static Result<IReadOnlyList<ClearRangesOperation>> ParseClearOperations(string operationsJson)
    {
        if (string.IsNullOrEmpty(operationsJson))
        {
            return Result.Fail("Operations JSON is required.");
        }

        try
        {
            var operations = JsonSerializer.Deserialize<List<ClearRangesOperation>>(operationsJson, JsonOptions);
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
