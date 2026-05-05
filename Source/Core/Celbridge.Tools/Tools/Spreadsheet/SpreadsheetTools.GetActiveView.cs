using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Returns the workbook's persisted view state: the active sheet, the full selection on
    /// that sheet (which may be non-contiguous when the user Ctrl+clicked multiple ranges),
    /// the active cell within the selection, and the scroll anchor. ranges is the full list;
    /// range is a convenience equal to ranges[0]. Use ranges to interpret a multi-selection
    /// the user made to point you at several distinct cell groups. Mirrors the parameters
    /// accepted by spreadsheet_set_active_view: pass ranges back via rangesJson to round-trip
    /// a multi-range selection. activeCell is the anchor cell (Excel's white cell within a
    /// multi-cell selection) and equals range when range is a single cell.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook to read.</param>
    /// <returns>JSON object with: sheet (string, active worksheet name), range (string, A1 notation, the first range in ranges), ranges (string[], the full selection in A1 notation; multiple entries represent a non-contiguous selection), activeCell (string, anchor cell within the selection), topLeftCell (string, scroll anchor cell).</returns>
    [McpServerTool(Name = "spreadsheet_get_active_view", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_active_view")]
    public partial CallToolResult GetActiveView(string resource)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var viewResult = reader.GetActiveView(workbookPath);
        if (viewResult.IsFailure)
        {
            return ErrorResult(viewResult.FirstErrorMessage);
        }

        return SuccessResult(SerializeJson(viewResult.Value));
    }
}
