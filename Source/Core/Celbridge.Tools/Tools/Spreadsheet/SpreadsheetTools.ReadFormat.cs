using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Reads cell formatting in the same shape accepted by spreadsheet_format_ranges.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to read formatting from.</param>
    /// <param name="range">A1 cell range. Empty reads the sheet's used range.</param>
    /// <returns>JSON object with the read range and a 2D array of FormatSpec objects. See guides_read(['spreadsheet_read_format']) for round-trip rules.</returns>
    [McpServerTool(Name = "spreadsheet_read_format", ReadOnly = true)]
    [ToolAlias("spreadsheet.read_format")]
    public partial CallToolResult ReadFormat(
        string resource,
        string sheet,
        string range = "")
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var readResult = reader.ReadFormat(workbookPath, sheet, rangeArgument);
        if (readResult.IsFailure)
        {
            return ToolError(readResult);
        }

        var readValue = readResult.Value;
        var json = SerializeJson(readValue);
        return ToolSuccess(json);
    }
}
