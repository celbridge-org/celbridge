using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Read the workbook's persisted view: active sheet, selection, active cell, scroll anchor.</summary>
    [McpServerTool(Name = "spreadsheet_get_active_view", ReadOnly = true)]
    [ToolAlias("spreadsheet.get_active_view")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> GetActiveView(string resource)
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
        var viewResult = reader.GetActiveView(stream);
        if (viewResult.IsFailure)
        {
            return ToolResponse.Error(viewResult);
        }

        var viewValue = viewResult.Value;
        var json = SerializeJson(viewValue);
        return ToolResponse.Success(json);
    }
}
