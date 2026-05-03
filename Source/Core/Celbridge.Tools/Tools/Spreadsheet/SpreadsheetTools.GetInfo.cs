using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Returns workbook overview information: every sheet's name, used range, row and column count,
    /// plus any defined named ranges. Cheap. Safe to call before a spreadsheet_read_sheet on a large workbook.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook to inspect.</param>
    /// <returns>JSON object with: sheets (array of {name, usedRange, rowCount, columnCount}), namedRanges (array of {name, refersTo, scope}). usedRange is null for empty sheets.</returns>
    [McpServerTool(Name = "spreadsheet_get_info", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_info")]
    public partial CallToolResult GetInfo(string resource)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var infoResult = reader.GetInfo(workbookPath);
        if (infoResult.IsFailure)
        {
            return ErrorResult(infoResult.FirstErrorMessage);
        }

        var info = infoResult.Value;
        return SuccessResult(SerializeJson(info));
    }
}
