using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Returns the workbook's persisted view state: active sheet, selection, active cell, scroll anchor.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook to read.</param>
    /// <returns>JSON object with sheet, range (first range), ranges (full selection, may be non-contiguous), activeCell (anchor), and topLeftCell (scroll anchor). See guides_read(['spreadsheet_set_active_view']).</returns>
    [McpServerTool(Name = "spreadsheet_get_active_view", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_active_view")]
    public partial CallToolResult GetActiveView(string resource)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var viewResult = reader.GetActiveView(workbookPath);
        if (viewResult.IsFailure)
        {
            return ToolError(viewResult);
        }

        var viewValue = viewResult.Value;
        var json = SerializeJson(viewValue);
        return ToolSuccess(json);
    }
}
