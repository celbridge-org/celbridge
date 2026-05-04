using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Reads cell styles from a sheet in an .xlsx workbook. Returns one SpreadsheetFormatSpec object per cell
    /// in the same shape accepted by spreadsheet_format_ranges, with most non-default properties included.
    /// Cells with no fill emit backgroundColor as the empty string, and theme/auto colours emit as the empty
    /// string, so feeding the output straight back into spreadsheet_format_ranges reproduces the source cell's
    /// fill and colour state on the destination (the empty string is the explicit clear/reset sentinel).
    /// Use this to inspect existing formatting or to capture styles before copying them to another range or
    /// sheet with spreadsheet_format_ranges.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook to read.</param>
    /// <param name="sheet">Name of the worksheet to read styles from.</param>
    /// <param name="range">A1-notation cell range to read (e.g. "A1:C3"). Empty string reads the sheet's used range. Do not include a sheet qualifier.</param>
    /// <returns>JSON object with: range (string, sheet-qualified range that was read), rows (2D array of format spec objects, one per cell, with null fields omitted and empty-string colours indicating no fill or default colour).</returns>
    [McpServerTool(Name = "spreadsheet_read_styles", ReadOnly = true)]
    [ToolAlias("spreadsheet.read_styles")]
    public partial CallToolResult ReadStyles(
        string resource,
        string sheet,
        string range = "")
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var readResult = reader.ReadStyles(workbookPath, sheet, rangeArgument);
        if (readResult.IsFailure)
        {
            return ErrorResult(readResult.FirstErrorMessage);
        }

        return SuccessResult(SerializeJson(readResult.Value));
    }
}
