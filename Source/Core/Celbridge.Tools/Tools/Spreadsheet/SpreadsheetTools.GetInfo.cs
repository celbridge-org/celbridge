using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Workbook overview: sheet list with dimensions and frozen panes, plus named ranges.</summary>
    [McpServerTool(Name = "spreadsheet_get_info", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_info")]
    public partial CallToolResult GetInfo(string resource)
    {
        const string ToolGuide = "spreadsheet_get_info";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }
        var workbookPath = resolveResult.Value;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var infoResult = reader.GetInfo(workbookPath);
        if (infoResult.IsFailure)
        {
            return ToolResponse.Error(infoResult, ToolGuide);
        }

        var info = infoResult.Value;
        var json = SerializeJson(info);
        return ToolResponse.Success(json);
    }
}
