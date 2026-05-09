using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Read the workbook's persisted view: active sheet, selection, active cell, scroll anchor.</summary>
    [McpServerTool(Name = "spreadsheet_get_active_view", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_active_view")]
    public partial CallToolResult GetActiveView(string resource)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var viewResult = reader.GetActiveView(workbookPath);
        if (viewResult.IsFailure)
        {
            return ToolResponse.Error(viewResult);
        }

        var viewValue = viewResult.Value;
        var json = SerializeJson(viewValue);
        return ToolResponse.Success(json);
    }
}
