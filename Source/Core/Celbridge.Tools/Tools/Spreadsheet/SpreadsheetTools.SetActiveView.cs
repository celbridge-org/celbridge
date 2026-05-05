using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_set_active_view: the sheet that was made
/// active and the selection, active-cell, or scroll-position values that were
/// applied. ranges echoes the full selection that was applied (one or more
/// A1 ranges).
/// </summary>
public record class SetActiveViewResult(
    string Sheet,
    string Range,
    IReadOnlyList<string> Ranges,
    string ActiveCell,
    string TopLeftCell);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Sets the persisted view state of a workbook so a user opening the file lands on a chosen
    /// sheet, selection, active cell, and scroll position. The named sheet is always made active.
    /// Selecting a cell auto-scrolls the viewport to it on open, so for "show this content" use a
    /// selection alone. Pass rangesJson for a non-contiguous selection (the Ctrl+click selection
    /// in Excel) — when supplied it takes precedence over range. activeCell controls the anchor
    /// cell within a multi-cell selection (Excel's white cell). topLeftCell is for cases where you
    /// want to control surrounding context (e.g. select row 50 but show rows 30-60). Frozen panes
    /// may clamp topLeftCell. If the workbook is open in the spreadsheet editor, the new view
    /// state is applied via the editor's normal external-reload path; the document tab does not
    /// close. Mirrors spreadsheet_get_active_view: pass its ranges back via rangesJson to
    /// round-trip a multi-range selection.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to make active. Required.</param>
    /// <param name="range">A1 cell or range to select on the target sheet. Empty string leaves the sheet's selection unchanged. When activeCell is also empty, the active cell defaults to range's first cell. Ignored when rangesJson is non-empty. Do not include a sheet qualifier.</param>
    /// <param name="rangesJson">JSON array of A1 cells or ranges that together form a non-contiguous selection (e.g. ["A7:B8", "A12:B13"]). When non-empty, takes precedence over range. Empty string or empty array defers to range. Each entry must omit the sheet qualifier.</param>
    /// <param name="activeCell">A1 single cell that becomes the anchor cell within the selection. Empty string defers to range / rangesJson (active cell becomes the first cell of the first range). When set with no selection, the selection becomes just this single cell. When set together with a selection, must lie inside one of the ranges. Must be a single cell, not a range.</param>
    /// <param name="topLeftCell">A1 single cell to anchor at the upper-left of the visible viewport on the target sheet. Empty string leaves the scroll position unchanged. Must be a single cell, not a range.</param>
    /// <returns>JSON object with fields: sheet (string), range (string, the first range that was applied or empty), ranges (string[], the full selection that was applied; empty when no selection was set), activeCell (string, the anchor cell that was applied or empty), topLeftCell (string, the scroll anchor that was applied or empty).</returns>
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
            return ErrorResult(resolveResult.FirstErrorMessage);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        var parseResult = ParseRangesJson(rangesJson);
        if (parseResult.IsFailure)
        {
            return ErrorResult(parseResult.FirstErrorMessage);
        }
        var ranges = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetSetActiveViewCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Range = range;
            command.Ranges = ranges;
            command.ActiveCell = activeCell;
            command.TopLeftCell = topLeftCell;
        });
        if (commandResult.IsError == true)
        {
            return commandResult;
        }

        var appliedRange = ranges.Count > 0 ? ranges[0] : range;
        var result = new SetActiveViewResult(sheet, appliedRange, ranges, activeCell, topLeftCell);
        return SuccessResult(SerializeJson(result));
    }

    private static Result<IReadOnlyList<string>> ParseRangesJson(string rangesJson)
    {
        if (string.IsNullOrEmpty(rangesJson))
        {
            return Result<IReadOnlyList<string>>.Ok(Array.Empty<string>());
        }

        try
        {
            var ranges = JsonSerializer.Deserialize<List<string>>(rangesJson);
            if (ranges is null)
            {
                return Result<IReadOnlyList<string>>.Fail("rangesJson must be a non-null array.");
            }
            return Result<IReadOnlyList<string>>.Ok(ranges);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<string>>.Fail($"Invalid rangesJson: {ex.Message}");
        }
    }
}
