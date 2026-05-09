using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Read per-cell formatting from a range as a 2D grid of format specs.</summary>
    [McpServerTool(Name = "spreadsheet_read_format", ReadOnly = true)]
    [ToolAlias("spreadsheet.read_format")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation")]
    public partial CallToolResult ReadFormat(
        string resource,
        string sheet,
        string range = "")
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var readResult = reader.ReadFormat(workbookPath, sheet, rangeArgument);
        if (readResult.IsFailure)
        {
            return ToolResponse.Error(readResult);
        }

        var readValue = readResult.Value;
        var json = SerializeJson(readValue);
        return ToolResponse.Success(json);
    }
}
