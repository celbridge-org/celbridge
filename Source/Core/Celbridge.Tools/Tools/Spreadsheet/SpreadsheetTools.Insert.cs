using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Insert empty rows or columns, shifting existing cells down or right.</summary>
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
        var commandResult = await ExecuteCommandAsync<IInsertRangesCommand, InsertRangesResult>(command =>
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

    private static Result<IReadOnlyList<InsertRangesOperation>> ParseInsertOperations(string operationsJson)
    {
        if (string.IsNullOrEmpty(operationsJson))
        {
            return Result.Fail("Operations JSON is required.");
        }

        try
        {
            var operations = JsonSerializer.Deserialize<List<InsertRangesOperation>>(operationsJson, JsonOptions);
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
