using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Returns workbook overview: per-sheet metadata (name, position, used range, dimensions, frozen panes) and named ranges.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <returns>JSON object with sheets and namedRanges arrays.</returns>
    [McpServerTool(Name = "spreadsheet_get_info", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_info")]
    public partial CallToolResult GetInfo(string resource)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var infoResult = reader.GetInfo(workbookPath);
        if (infoResult.IsFailure)
        {
            return ToolError(infoResult);
        }

        var info = infoResult.Value;
        var json = SerializeJson(info);
        return ToolSuccess(json);
    }
}
