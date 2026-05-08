using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Sets the persisted view state of a workbook (active sheet, selection, active cell, scroll anchor).
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to make active. Required.</param>
    /// <param name="range">A1 cell or range to select. Empty leaves selection unchanged. Ignored when rangesJson is set.</param>
    /// <param name="rangesJson">JSON array of A1 cells or ranges forming a non-contiguous selection. Takes precedence over range when non-empty.</param>
    /// <param name="activeCell">Single A1 cell that anchors the selection. Empty defers to the first range. When set with a selection, must lie inside one of the ranges.</param>
    /// <param name="topLeftCell">Single A1 cell to scroll to the upper-left of the viewport. Empty leaves the scroll position unchanged.</param>
    /// <returns>JSON object echoing the applied sheet, range, ranges, activeCell, and topLeftCell. See guides_read(['spreadsheet_set_active_view']) for selection semantics and how it round-trips with spreadsheet_get_active_view.</returns>
    [McpServerTool(Name = "spreadsheet_set_active_view")]
    [ToolAlias("spreadsheet.set_active_view")]
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
            return ToolError(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        var parseResult = ParseRangesJson(rangesJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
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
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
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
