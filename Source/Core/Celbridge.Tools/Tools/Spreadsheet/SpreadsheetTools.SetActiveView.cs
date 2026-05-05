using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_set_active_view: the sheet that was made
/// active and any selection, active-cell, or scroll-position values that were
/// applied.
/// </summary>
public record class SetActiveViewResult(string Sheet, string Range, string ActiveCell, string TopLeftCell);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Sets the persisted view state of a workbook so a user opening the file lands on a chosen
    /// sheet, selection, active cell, and scroll position. The named sheet is always made active.
    /// Selecting a cell auto-scrolls the viewport to it on open, so for "show this content" use a
    /// selection alone. activeCell controls the anchor cell within a multi-cell selection (Excel's
    /// white cell). topLeftCell is for cases where you want to control surrounding context
    /// (e.g. select row 50 but show rows 30-60). Frozen panes may clamp topLeftCell.
    /// If the workbook is open in the spreadsheet editor, the new view state is applied via
    /// the editor's normal external-reload path; the document tab does not close.
    /// Mirrors spreadsheet_get_active_view: the get response can be passed straight back to set.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to make active. Required.</param>
    /// <param name="range">A1 cell or range to select on the target sheet. Empty string leaves the sheet's selection unchanged. When activeCell is also empty, the active cell defaults to range's first cell. Do not include a sheet qualifier.</param>
    /// <param name="activeCell">A1 single cell that becomes the anchor cell within the selection. Empty string defers to range. When set with range empty, the selection becomes just this single cell. When set with range non-empty, must lie inside range. Must be a single cell, not a range.</param>
    /// <param name="topLeftCell">A1 single cell to anchor at the upper-left of the visible viewport on the target sheet. Empty string leaves the scroll position unchanged. Must be a single cell, not a range.</param>
    /// <returns>JSON object with fields: sheet (string), range (string, the selection that was applied or empty), activeCell (string, the anchor cell that was applied or empty), topLeftCell (string, the scroll anchor that was applied or empty).</returns>
    [McpServerTool(Name = "spreadsheet_set_active_view")]
    [ToolAlias("spreadsheet.set_active_view")]
    public async partial Task<CallToolResult> SetActiveView(
        string resource,
        string sheet,
        string range = "",
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

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISpreadsheetSetActiveViewCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Range = range;
            command.ActiveCell = activeCell;
            command.TopLeftCell = topLeftCell;
        });
        if (commandResult.IsError == true)
        {
            return commandResult;
        }

        return SuccessResult(SerializeJson(new SetActiveViewResult(sheet, range, activeCell, topLeftCell)));
    }
}
