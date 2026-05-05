using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Returns the workbook's persisted view state: the active sheet, the selection on that
    /// sheet, the active cell within the selection, and the scroll anchor. Mirrors the
    /// parameters accepted by spreadsheet_set_active_view, so an agent can read the current
    /// view, modify a field, and pass the values straight back to set_active_view to
    /// round-trip. activeCell is the anchor cell (Excel's white cell within a multi-cell
    /// selection) and equals range when range is a single cell.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook to read.</param>
    /// <returns>JSON object with: sheet (string, active worksheet name), range (string, A1-notation selection on that sheet), activeCell (string, anchor cell within the selection), topLeftCell (string, scroll anchor cell).</returns>
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
