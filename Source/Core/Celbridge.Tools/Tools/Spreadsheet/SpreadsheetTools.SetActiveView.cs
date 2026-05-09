using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Set the workbook's persisted view: active sheet, selection, active cell, scroll anchor.</summary>
    [McpServerTool(Name = "spreadsheet_set_active_view")]
    [ToolAlias("spreadsheet.set_active_view")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> SetActiveView(
        string resource,
        string sheet,
        string range = "",
        string rangesJson = "",
        string activeCell = "",
        string topLeftCell = "")
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        var parseResult = ParseRangesJson(rangesJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var ranges = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISetActiveViewCommand, SetActiveViewResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Range = range;
            command.Ranges = ranges;
            command.ActiveCell = activeCell;
            command.TopLeftCell = topLeftCell;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }

    private static Result<IReadOnlyList<string>> ParseRangesJson(string rangesJson)
    {
        if (string.IsNullOrEmpty(rangesJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var ranges = JsonSerializer.Deserialize<List<string>>(rangesJson);
            if (ranges is null)
            {
                return Result.Fail("rangesJson must be a non-null array.");
            }
            return ranges;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid rangesJson: {ex.Message}");
        }
    }
}
