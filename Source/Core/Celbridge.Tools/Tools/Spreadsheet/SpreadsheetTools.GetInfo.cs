using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Workbook overview: sheet list with dimensions and frozen panes, plus named ranges.</summary>
    [McpServerTool(Name = "spreadsheet_get_info", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_info")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_workflows")]
    public async partial Task<CallToolResult> GetInfo(string resource)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        var openResult = await OpenWorkbookStreamAsync(workbookResource);
        if (openResult.IsFailure)
        {
            return ToolResponse.Error(openResult);
        }

        using var stream = openResult.Value;
        var reader = GetRequiredService<ISpreadsheetReader>();
        var infoResult = reader.GetInfo(stream);
        if (infoResult.IsFailure)
        {
            return ToolResponse.Error(infoResult);
        }

        var info = infoResult.Value;
        var json = SerializeJson(info);
        return ToolResponse.Success(json);
    }
}
